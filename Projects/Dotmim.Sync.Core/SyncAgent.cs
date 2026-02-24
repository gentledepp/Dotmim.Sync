using Wormhole.Sync.Enumerations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{

    /// <summary>
    /// Sync agent. It's the sync orchestrator
    /// Knows both the Sync Server provider and the Sync Client provider.
    /// </summary>
    public partial class SyncAgent
    {
        private readonly object balanceLock = new();
        private bool syncInProgress;
        private bool checkUpgradeDone;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAgent"/> class.
        /// Creates a synchronization agent that will handle a full synchronization between a client and a server.
        /// </summary>
        /// <param name="clientProvider">Local Provider connecting to your client database.</param>
        /// <param name="serverProvider">Local Provider connecting to your server database.</param>
        /// <param name="options">Sync Options defining options used by your local and remote provider.</param>
        public SyncAgent(CoreProvider clientProvider, CoreProvider serverProvider, SyncOptions options = default)
            : this()
        {
            Guard.ThrowIfNull(clientProvider);
            Guard.ThrowIfNull(serverProvider);

            options ??= new SyncOptions();

            // Affect local and remote orchestrators
            this.LocalOrchestrator = new LocalOrchestrator(clientProvider, options);
            this.RemoteOrchestrator = new RemoteOrchestrator(serverProvider, options);

            this.EnsureOptionsAndSetupInstances();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAgent"/> class.
        /// Creates a synchronization agent that will handle a full synchronization between a client and a server.
        /// </summary>
        /// <param name="clientProvider">local provider to your client database.</param>
        /// <param name="remoteOrchestrator">Remote Orchestrator already configured with a SyncProvider.</param>
        /// <param name="options">Sync Options defining options used by your local provider (and remote provider if type of remoteOrchestrator is not a WebRemoteOrchestrator).</param>
        public SyncAgent(CoreProvider clientProvider, RemoteOrchestrator remoteOrchestrator, SyncOptions options = default)
            : this()
        {
            Guard.ThrowIfNull(clientProvider);
            Guard.ThrowIfNull(remoteOrchestrator);

            if (options == default)
                options = new SyncOptions();

            // Override remote orchestrator options, setup and scope name
            remoteOrchestrator.Options = options;

            var localOrchestrator = new LocalOrchestrator(clientProvider, options);

            this.LocalOrchestrator = localOrchestrator;
            this.RemoteOrchestrator = remoteOrchestrator;
            this.EnsureOptionsAndSetupInstances();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAgent"/> class.
        /// Creates a synchronization agent that will handle a full synchronization between a client and a server.
        /// </summary>
        /// <param name="localOrchestrator">Local Orchestrator already configured with a SyncProvider.</param>
        /// <param name="remoteOrchestrator">Remote Orchestrator already configured with a SyncProvider.</param>
        public SyncAgent(LocalOrchestrator localOrchestrator, RemoteOrchestrator remoteOrchestrator)
            : this()
        {
            Guard.ThrowIfNull(localOrchestrator);
            Guard.ThrowIfNull(remoteOrchestrator);

            this.LocalOrchestrator = localOrchestrator;
            this.RemoteOrchestrator = remoteOrchestrator;
            this.EnsureOptionsAndSetupInstances();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAgent"/> class.
        /// </summary>
        private SyncAgent() { }

        /// <summary>
        /// Occurs when sync is starting, ending
        /// </summary>
        public event EventHandler<SyncSessionStateEventArgs> SessionStateChanged;

        /// <summary>
        /// Gets or sets defines the state that a synchronization session is in.
        /// </summary>
        public SyncSessionState SessionState { get; set; } = SyncSessionState.Ready;

        /// <summary>
        /// Gets or Sets the local orchestrator.
        /// </summary>
        public LocalOrchestrator LocalOrchestrator { get; set; }

        /// <summary>
        /// Gets or sets get or Sets the remote orchestrator.
        /// </summary>
        public RemoteOrchestrator RemoteOrchestrator { get; set; }

        /// <summary>
        /// Gets the options used on this sync process.
        /// </summary>
        public SyncOptions Options => this.LocalOrchestrator?.Options;

        /// <summary>
        /// Gets or sets the list of migration names that this client app version supports.
        /// Set by the developer to declare which schema migrations the client app has been updated to handle.
        /// Example: ["20260217_titlecolumns", "20260301_newprefs"]
        /// </summary>
        public List<string> SupportedMigrations { get; set; } = new();

        /// <summary>
        /// Shortcut to Apply changed conflict occured if remote orchestrator supports it.
        /// </summary>
        public void OnApplyChangesConflictOccured(Action<ApplyChangesConflictOccuredArgs> action)
        {
            if (this.RemoteOrchestrator == null)
                throw new InvalidRemoteOrchestratorException();

            this.RemoteOrchestrator.OnApplyChangesConflictOccured(action);
        }

        /// <summary>
        /// Shortcut to Apply changed conflict occured if remote orchestrator supports it.
        /// </summary>
        public void OnApplyChangesConflictOccured(Func<ApplyChangesConflictOccuredArgs, Task> action)
        {
            if (this.RemoteOrchestrator == null)
                throw new InvalidRemoteOrchestratorException();

            this.RemoteOrchestrator.OnApplyChangesConflictOccured(action);
        }

        /// <summary>
        /// Launch a synchronization with the specified mode.
        /// </summary>
        public async Task<SyncResult> SynchronizeAsync(string scopeName, SyncSetup setup, SyncType syncType, SyncParameters parameters, IProgress<ProgressArgs> progress = default, CancellationToken cancellationToken = default)
        {
            ClientSyncChanges clientSyncChanges = null;
            ServerSyncChanges serverSyncChanges = null;
            SyncException syncException = null;
            var useOptimizedFlow = false;

            // checkpoints dates
            var startTime = DateTime.UtcNow;
            var completeTime = DateTime.UtcNow;

            // Create a logger
            this.Options.Logger = this.Options.Logger ?? new SyncLogger().AddDebug();

            // Lock sync to prevent multi call to sync at the same time
            this.LockSync();

            // Context, used to back and forth data between servers
            var context = new SyncContext(Guid.NewGuid(), scopeName)
            {
                // if any parameters, set in context
                Parameters = parameters,

                // set sync type (Normal, Reinitialize, ReinitializeWithUpload)
                SyncType = syncType
            };

            // Result, with sync results stats.
            var result = new SyncResult(context.SessionId)
            {
                // set start time
                StartTime = startTime,
                CompleteTime = completeTime,
            };

            this.SessionState = SyncSessionState.Synchronizing;
            this.SessionStateChanged?.Invoke(this, new SyncSessionStateEventArgs(this.SessionState));


            // await Task.Run(async () =>
            // {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                if (setup != null)
                {
                    var remoteOrchestratorType = this.RemoteOrchestrator.GetType();
                    var providerType = remoteOrchestratorType.Name;
                    if (string.Equals(providerType, "webclientorchestrator", SyncGlobalization.DataSourceStringComparison) || string.Equals(providerType, "webremotetorchestrator", SyncGlobalization.DataSourceStringComparison))
                        throw new Exception("Do not set Tables (or SyncSetup) from your client. Please use SyncAgent, without any Tables or SyncSetup. The tables will come from the server side");
                }

                // Begin session
                context = await this.LocalOrchestrator.InternalBeginSessionAsync(context, progress, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                // no need to check on every call to SynchronizeAsync
                if (!this.checkUpgradeDone)
                {
                    var needToUpgrade = await this.LocalOrchestrator.NeedsToUpgradeAsync(context).ConfigureAwait(false);

                    if (needToUpgrade)
                        await this.LocalOrchestrator.InternalUpgradeAsync(context, default, default, progress, cancellationToken).ConfigureAwait(false);

                    needToUpgrade = await this.RemoteOrchestrator.NeedsToUpgradeAsync(context).ConfigureAwait(false);

                    if (needToUpgrade)
                        await this.RemoteOrchestrator.InternalUpgradeAsync(context, default, default, progress, cancellationToken).ConfigureAwait(false);

                    this.checkUpgradeDone = true;
                }

                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                // --------------------------------------------------------------
                // OPTIMIZATION: Check if we can use optimized sync protocol
                // --------------------------------------------------------------
                bool canUseOptimizedFlow = false;
                ScopeInfo cScopeInfo = null;
                ScopeInfoClient cScopeInfoClient = null;
                ScopeInfo sScopeInfo = null;
                ConflictResolutionPolicy serverResolutionPolicy = ConflictResolutionPolicy.ServerWins;
                SyncOperation? operation = null;
                bool isClientSchemaValid = true;

                // Try to get local scope info to check for optimization
                (context, cScopeInfo) = await this.LocalOrchestrator.InternalEnsureScopeInfoAsync(context, default, default, progress, cancellationToken).ConfigureAwait(false);
                (context, cScopeInfoClient) = await this.LocalOrchestrator.InternalEnsureScopeInfoClientAsync(context, default, default, progress, cancellationToken).ConfigureAwait(false);

                // Check if client has unprovisioned migrations → force traditional flow.
                // We must avoid attempting optimized flow when reprovision is needed,
                // because the server processes the sync even when rejecting (schemaValid=false)
                // and sets AppliedBatchesSuccessfully=true in the session cache. A subsequent
                // traditional flow call then gets a stub response with no BatchInfo → NullRef.
                // NOTE: SupportedMigrations is only written to cScopeInfoClient AFTER
                // reprovision, so the DB-stored value reflects what's actually provisioned.
                var hasPendingMigrations = false;
                if (this.SupportedMigrations != null && this.SupportedMigrations.Count > 0
                    && cScopeInfo?.Setup != null)
                {
                    var clientProvisioned = cScopeInfoClient.GetSupportedMigrationsList();
                    hasPendingMigrations = this.SupportedMigrations.Any(m => !clientProvisioned.Contains(m));
                }

                // check if the server supports unified batching
                if (this.Options.UseUnifiedBatching)
                    context.UseUnifiedBatching = true;

                // Check if remote orchestrator supports optimization
                if (this.Options.UseOptimizedFlow && this.RemoteOrchestrator is IIncrementalSyncOrchestrator optimized)
                {
                    canUseOptimizedFlow = optimized.CanUseOptimizedSync(cScopeInfo, cScopeInfoClient);
                }

                var clientIsNew = cScopeInfoClient.IsNewScope || cScopeInfo.Schema == null;

                // Skip optimized flow when there are pending migrations — we need the
                // traditional flow to reprovision before syncing (and can't fall back
                // from optimized flow without double-applying changes on the server).
                useOptimizedFlow = !clientIsNew && canUseOptimizedFlow && syncType == SyncType.Normal && !hasPendingMigrations;

                if (useOptimizedFlow)
                {

                    // On local orchestrator, get local changes
                    (context, clientSyncChanges) = await this.LocalOrchestrator.InternalGetChangesAsync(cScopeInfo, context, cScopeInfoClient,
                        default, default, progress, cancellationToken).ConfigureAwait(false);
                 
                    // send optimized
                    (context, isClientSchemaValid, operation, sScopeInfo, cScopeInfo, serverSyncChanges, serverResolutionPolicy) =
                        await ((IIncrementalSyncOrchestrator)this.RemoteOrchestrator).SynchronizeOptimizedAsync(
                            cScopeInfoClient, cScopeInfo, context, clientSyncChanges, default, default, progress, cancellationToken).ConfigureAwait(false);
                   
                    // if anything went wrong, fall back to default protocol
                    if (!isClientSchemaValid || 
                        (operation != SyncOperation.Normal && operation != SyncOperation.Reinitialize && operation != SyncOperation.ReinitializeWithUpload))
                        useOptimizedFlow = false;

                    if (operation == SyncOperation.Reinitialize)
                        context.SyncType = SyncType.Reinitialize;
                    else if (operation == SyncOperation.ReinitializeWithUpload)
                        context.SyncType = SyncType.ReinitializeWithUpload;
                }
                
                if (!useOptimizedFlow)
                {
                    // Traditional flow: Begin session on remote
                    context = await this.RemoteOrchestrator.InternalBeginSessionAsync(context, progress, cancellationToken).ConfigureAwait(false);
                
                    // on remote orchestrator, get Server scope
                    var shouldProvision = false;
                    if(sScopeInfo is null) // maybe we already retrieved it from the optimized sync attmept
                        (context, sScopeInfo, shouldProvision) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(context, setup, false, default, default, progress, cancellationToken).ConfigureAwait(false);

                    // -----------------------------------------------------------
                    // Schema evolution: auto-reprovision if the server has
                    // migrations that the client supports but hasn't provisioned
                    // locally yet (proven by schema hash mismatch → we're in
                    // traditional flow).
                    // -----------------------------------------------------------
                    if (this.SupportedMigrations != null && this.SupportedMigrations.Count > 0
                        && sScopeInfo != null && !string.IsNullOrEmpty(sScopeInfo.Migrations))
                    {
                        var serverMigrations = sScopeInfo.GetMigrationsList();
                        var clientNeedsReprovision = serverMigrations.Any(m => this.SupportedMigrations.Contains(m));

                        if (clientNeedsReprovision)
                        {
                            // Determine which tables have schema changes and need re-downloading
                            // IMPORTANT: Do that BEFORE re-provisioning the client, since otherwise
                            // the cScopeInfo will already have the same schema as the server
                            var migration = new Migration(cScopeInfo, sScopeInfo);
                            var migrationResult = migration.Compare();
                            var changedTables = migrationResult.GetTablesWithSchemaChanges();

                            // Record which migrations are now provisioned so we don't repeat
                            cScopeInfoClient.SetSupportedMigrationsList(this.SupportedMigrations);

                            if (changedTables.Count > 0)
                                cScopeInfoClient.SetReinitTables(changedTables);

                            // Persist reinit state immediately so it survives crashes.
                            // If the sync crashes after reprovisioning but before completion,
                            // the next sync will still see the ReinitTables flag.
                            using (var scopeRunner = await this.LocalOrchestrator.GetConnectionAsync(
                                       context, SyncMode.NoTransaction, SyncStage.ScopeWriting,
                                       default, default, progress, cancellationToken).ConfigureAwait(false))
                            {
                                await using (scopeRunner.ConfigureAwait(false))
                                {
                                    (context, cScopeInfoClient) = await this.LocalOrchestrator.InternalSaveScopeInfoClientAsync(
                                        cScopeInfoClient, context,
                                        scopeRunner.Connection, scopeRunner.Transaction,
                                        scopeRunner.Progress, scopeRunner.CancellationToken).ConfigureAwait(false);
                                }
                            }

                            // only now perform the local schema changes
                            var provision = SyncProvision.StoredProcedures | SyncProvision.Triggers;

                            (context, _) = await this.LocalOrchestrator.InternalDeprovisionAsync(
                                cScopeInfo, context, provision,
                                default, default, progress, cancellationToken).ConfigureAwait(false);

                            (context, cScopeInfo) = await this.LocalOrchestrator.InternalProvisionClientAsync(
                                sScopeInfo, cScopeInfo, context, provision, true,
                                default, default, progress, cancellationToken).ConfigureAwait(false);

                        }
                    }

                    // Schema evolution: if client is ahead of server, fall back to server's setup
                    if (this.SupportedMigrations != null && this.SupportedMigrations.Count > 0
                        && sScopeInfo?.Setup != null)
                    {
                        var serverMigs = sScopeInfo.GetMigrationsList();
                        var clientStillAhead = this.SupportedMigrations.Any(m => !serverMigs.Contains(m));

                        if (clientStillAhead)
                            setup = sScopeInfo.Setup;
                    }

                    var isConflicting = false;
                    (context, isConflicting, sScopeInfo) = await this.RemoteOrchestrator.InternalIsConflictingSetupAsync(context, setup, sScopeInfo, default, default, progress, cancellationToken).ConfigureAwait(false);

                    // Check if we have a problem with the SyncSetup local and the one coming from server
                    // Let a chance to the user to update the local setup accordingly to the server one
                    isConflicting = false;
                    (context, isConflicting, cScopeInfo, sScopeInfo) = await this.LocalOrchestrator.InternalIsConflictingSetupAsync(context, setup, cScopeInfo, sScopeInfo, default, default, progress, cancellationToken).ConfigureAwait(false);

                    if (isConflicting)
                    {
                        context.ProgressPercentage = 1;
                        context = await this.LocalOrchestrator.InternalEndSessionAsync(context, result, null, null, progress, cancellationToken).ConfigureAwait(false);
                        return result;
                    }
                    
                    // Register local scope id
                    context.ClientId = cScopeInfoClient.Id;

                    if (cancellationToken.IsCancellationRequested)
                        cancellationToken.ThrowIfCancellationRequested();

                    // we may have created the scope tables and fail before provision
                    // check if we have some scope info clients already saved
                    if (!shouldProvision)
                        shouldProvision = await this.RemoteOrchestrator.InternalShouldProvisionServerAsync(sScopeInfo, context, default, default, progress, cancellationToken).ConfigureAwait(false);

                    // If we just have create the server scope, we need to provision it
                    // the WebServerAgent will do this setp on the GetServrScopeInfoAsync task, just before
                    // So far, on Http mode, this if() will not be called
                    if (shouldProvision)
                    {
                        // 2) Provision
                        var provision = SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers;
                        (context, sScopeInfo) = await this.RemoteOrchestrator.InternalProvisionServerAsync(sScopeInfo, context, provision, false, default, default, progress, cancellationToken).ConfigureAwait(false);
                    }

                    if (cancellationToken.IsCancellationRequested)
                        cancellationToken.ThrowIfCancellationRequested();
                    
                    // Get operation from server
                    if(operation is null) // maybe we already got the operation from our optimized sync attempt
                        (context, operation) = await this.RemoteOrchestrator.InternalGetOperationAsync(sScopeInfo, cScopeInfo, cScopeInfoClient, context, default, default, progress, cancellationToken).ConfigureAwait(false);

                    if (operation != SyncOperation.Normal)
                    {
                        if (operation == SyncOperation.AbortSync)
                        {
                            context.ProgressPercentage = 1;
                            context = await this.LocalOrchestrator.InternalEndSessionAsync(context, result, null, null, progress, cancellationToken).ConfigureAwait(false);
                            return result;
                        }

                        // override order to Deprovision client
                        if (operation == SyncOperation.DeprovisionAndSync && cScopeInfo.Setup != null && cScopeInfo.Setup.HasTables)
                        {
                            var provision = SyncProvision.StoredProcedures | SyncProvision.Triggers;
                            (context, _) = await this.LocalOrchestrator.InternalDeprovisionAsync(cScopeInfo, context, provision, default, default, progress, cancellationToken).ConfigureAwait(false);
                            (context, cScopeInfo) = await this.LocalOrchestrator.InternalProvisionClientAsync(sScopeInfo, cScopeInfo, context, provision, false, default, default, progress, cancellationToken).ConfigureAwait(false);
                        }

                        if (operation == SyncOperation.DropAllAndSync)
                        {
                            await this.LocalOrchestrator.DropAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

                            // Recreated scope info
                            (context, cScopeInfo) = await this.LocalOrchestrator.InternalEnsureScopeInfoAsync(context, default, default, progress, cancellationToken).ConfigureAwait(false);
                        }

                        if (operation == SyncOperation.DropAllAndExit)
                        {
                            await this.LocalOrchestrator.DropAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                            context.ProgressPercentage = 1;
                            context = await this.LocalOrchestrator.InternalEndSessionAsync(context, result, null, null, progress, cancellationToken).ConfigureAwait(false);
                            return result;
                        }

                        if (operation == SyncOperation.Reinitialize)
                        {
                            context.SyncType = SyncType.Reinitialize;
                        }
                        else if (operation == SyncOperation.ReinitializeWithUpload)
                        {
                            context.SyncType = SyncType.ReinitializeWithUpload;
                        }
                    }

                    // if client is new or schema does not exists or scope name is a new one
                    // We need to get the scope from server
                    if (clientIsNew)
                    {
                        // Provision local database
                        var provision = SyncProvision.Table | SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers;
                        (context, cScopeInfo) = await this.LocalOrchestrator.InternalProvisionClientAsync(sScopeInfo, cScopeInfo, context, provision, false, default, default, progress, cancellationToken).ConfigureAwait(false);
                    }

                    setup ??= cScopeInfo.Setup;

                    if (cancellationToken.IsCancellationRequested)
                        cancellationToken.ThrowIfCancellationRequested();

                    // Before call the changes from localorchestrator, check if we are outdated
                    if (sScopeInfo != null && context.SyncType != SyncType.Reinitialize && context.SyncType != SyncType.ReinitializeWithUpload)
                    {
                        var isOutDated = false;
                        (context, isOutDated) = await this.LocalOrchestrator.InternalIsOutDatedAsync(context, cScopeInfoClient, sScopeInfo, cancellationToken: cancellationToken).ConfigureAwait(false);

                        // if client does not change SyncType to Reinitialize / ReinitializeWithUpload on SyncInterceptor, we raise an error
                        // otherwise, we are outdated, but we can continue, because we have a new mode.
                        if (isOutDated)
                            Debug.WriteLine($"Client id outdated, but we change mode to {context.SyncType}");
                    }

                    context.ProgressPercentage = 0.1;

                    // On local orchestrator, get local changes
                    (context, clientSyncChanges) = await this.LocalOrchestrator.InternalGetChangesAsync(cScopeInfo, context, cScopeInfoClient,
                        default, default, progress, cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                        cancellationToken.ThrowIfCancellationRequested();

                    // If we are in reinit mode, force scope last server sync timestamp & scope last client sync timestamp to null
                    if (context.SyncType == SyncType.Reinitialize || context.SyncType == SyncType.ReinitializeWithUpload)
                    {
                        cScopeInfoClient.LastServerSyncTimestamp = null;
                        cScopeInfoClient.LastSyncTimestamp = null;
                    }

                    // Get if we need to get all rows from the datasource
                    var fromScratch = cScopeInfoClient.IsNewScope || context.SyncType == SyncType.Reinitialize || context.SyncType == SyncType.ReinitializeWithUpload;

                    // IF is new and we have a snapshot directory, try to apply a snapshot
                    if (fromScratch && sScopeInfo is not null)
                    {
                        ServerSyncChanges snapshotServerSyncChanges;
                        (context, snapshotServerSyncChanges)
                            = await this.RemoteOrchestrator.InternalGetSnapshotAsync(sScopeInfo, context, default, default, progress, cancellationToken).ConfigureAwait(false);

                        // Apply snapshot
                        if (snapshotServerSyncChanges?.ServerBatchInfo != null)
                        {
                            (context, clientSyncChanges, cScopeInfoClient) = await this.LocalOrchestrator.InternalApplySnapshotAsync(
                                                cScopeInfo, cScopeInfoClient, context, snapshotServerSyncChanges, clientSyncChanges,
                                                default, default, progress, cancellationToken).ConfigureAwait(false);

                            result.SnapshotChangesAppliedOnClient = clientSyncChanges.ClientChangesApplied;

                            // CRITICAL FIX: Set the LastServerSyncTimestamp to the snapshot's creation timestamp
                            // This enables incremental sync for changes that occurred after the snapshot was created
                            // Without this, the client would miss deletions and updates that happened between
                            // snapshot creation and the current sync (especially important for ReinitializeWithUpload)
                            // The RemoteOrchestrator will use this timestamp to fetch only delta changes via _changes
                            // stored procedures instead of re-downloading all data via _initialize procedures
                            if (snapshotServerSyncChanges.RemoteClientTimestamp > 0)
                            {
                                cScopeInfoClient.LastServerSyncTimestamp = snapshotServerSyncChanges.RemoteClientTimestamp;
                            }
                        }
                    }
                }

                // Get if we have already applied a snapshot, so far we don't need to reset table even if we are i Reinitialize Mode
                var snapshotApplied = result.SnapshotChangesAppliedOnClient != null;

                context.ProgressPercentage = 0.3;

                // re-set parameters
                cScopeInfoClient.Parameters = parameters;

                // Use optimized flow if available and conditions are met
                if (useOptimizedFlow && this.RemoteOrchestrator is IIncrementalSyncOrchestrator)
                {
                    // noop
                }
                else
                {
                    // Traditional flow
                    (context, serverSyncChanges, serverResolutionPolicy) =
                        await this.RemoteOrchestrator.InternalApplyThenGetChangesAsync(
                            cScopeInfoClient, cScopeInfo, context, clientSyncChanges, default, default, progress, cancellationToken).ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                // apply is 25%
                context.ProgressPercentage = 0.75;

                (context, clientSyncChanges, cScopeInfoClient) = await this.LocalOrchestrator.InternalApplyChangesAsync(
                        cScopeInfo, cScopeInfoClient, context, serverSyncChanges, clientSyncChanges, serverResolutionPolicy, snapshotApplied, default, default,
                        progress, cancellationToken).ConfigureAwait(false);

                completeTime = DateTime.UtcNow;
                this.LocalOrchestrator.CompleteTime = completeTime;
                this.RemoteOrchestrator.CompleteTime = completeTime;

                result.CompleteTime = completeTime;

                // All clients changes selected
                result.ClientChangesSelected = clientSyncChanges.ClientChangesSelected;
                result.ServerChangesSelected = serverSyncChanges.ServerChangesSelected;
                result.ChangesAppliedOnClient = clientSyncChanges.ClientChangesApplied;
                result.ChangesAppliedOnServer = serverSyncChanges.ServerChangesApplied;

                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception)
            {
                // First we log the error before adding a new layer
                this.Options.Logger.LogError(SyncEventsId.Exception, exception, exception.Message);

                // Report errors to server when using optimized flow
                if (useOptimizedFlow) 
                    await this.ReportErrorsToServerAsync(progress, cancellationToken, context, exception);

                if (exception is SyncException ex)
                    syncException = ex;
                else
                    syncException = new SyncException(exception);

                throw syncException;
            }
            finally
            {
                context.ProgressPercentage = 1;
                try
                {
                    this.LocalOrchestrator.InternalEndSessionAsync(context, result, clientSyncChanges, syncException, progress, cancellationToken).Forget();
                    this.RemoteOrchestrator.InternalEndSessionAsync(context, result, serverSyncChanges, syncException, progress, cancellationToken).Forget();
                }
                catch
                {
                }

                // Report any sync exceptions to server when using optimized flow (fire-and-forget)
                if (useOptimizedFlow) 
                    await this.ReportErrorsToServerAsync(progress, cancellationToken, context, syncException);
                
                // End the current session
                this.SessionState = SyncSessionState.Ready;
                this.SessionStateChanged?.Invoke(this, new SyncSessionStateEventArgs(this.SessionState));

                // unlock sync since it's over
                GC.Collect();
                GC.WaitForPendingFinalizers();
                this.UnlockSync();
            }

            return result;
        }

        /// <summary>
        /// Reports sync errors to the server for analytics and debugging (fire-and-forget).
        /// Only sends reports when server supports error reporting capability.
        /// </summary>
        private async Task ReportErrorsToServerAsync(IProgress<ProgressArgs> progress, CancellationToken cancellationToken,
            SyncContext context, Exception exception)
        {
            if(this.RemoteOrchestrator is IIncrementalSyncOrchestrator incremental && exception is not null)
            {
                try
                {
                    await incremental.ReportSyncErrorAsync(context, exception, null, progress, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Error reporting is best effort - don't fail the original exception
                }
            }
        }

        /// <summary>
        /// Gets the string representation of the SyncAgent, by outputing the local and remote orchestrator names.
        /// </summary>
        public override string ToString()
        {
            var from = this.LocalOrchestrator?.ToString();
            var to = this.RemoteOrchestrator?.ToString();

            if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                return $"[{from}] => [{to}]";

            return base.ToString();
        }

        /// <summary>
        /// Ensure Options and Setup instances are the same on local orchestrator and remote orchestrator.
        /// </summary>
        private void EnsureOptionsAndSetupInstances()
        {
            // if we have a remote orchestrator with different options, raise an error
            if (this.RemoteOrchestrator.Options != null && this.RemoteOrchestrator.Options != this.LocalOrchestrator.Options)
                throw new OptionsReferencesAreNotSameExecption();
            else if (this.RemoteOrchestrator.Options == null)
                this.RemoteOrchestrator.Options = this.LocalOrchestrator.Options;
        }

        /// <summary>
        /// Lock sync to prevent multi call to sync at the same time.
        /// </summary>
        private void LockSync()
        {
            lock (this.balanceLock)
            {
                if (this.syncInProgress)
                    throw new AlreadyInProgressException();

                this.syncInProgress = true;
            }
        }

        /// <summary>
        /// Unlock sync to be able to launch a new sync.
        /// </summary>
        private void UnlockSync()
        {
            // Enf sync from local provider
            lock (this.balanceLock)
            {
                this.syncInProgress = false;
            }
        }
    }
}