using Wormhole.Sync.Enumerations;
using System;
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

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(SyncOptions.DefaultScopeName, (SyncSetup)null, SyncType.Normal, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(SyncOptions.DefaultScopeName, (SyncSetup)null, SyncType.Normal, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(SyncType syncType, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(SyncOptions.DefaultScopeName, (SyncSetup)null, syncType, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(SyncType syncType, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(SyncOptions.DefaultScopeName, (SyncSetup)null, syncType, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(scopeName, (SyncSetup)null, SyncType.Normal, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(scopeName, (SyncSetup)null, SyncType.Normal, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, SyncType syncType, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(scopeName, (SyncSetup)null, syncType, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, SyncType syncType, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(scopeName, (SyncSetup)null, syncType, parameters, progress, cancellationToken);

        // ---------------------------------------------
        // string[] tables
        // ---------------------------------------------

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="tables">Tables list to synchronize.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string[] tables, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default) =>
            this.SynchronizeAsync(SyncOptions.DefaultScopeName, new SyncSetup(tables), SyncType.Normal, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="tables">Tables list to synchronize.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string[] tables, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default) =>
            this.SynchronizeAsync(SyncOptions.DefaultScopeName, new SyncSetup(tables), SyncType.Normal, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="tables">Tables list to synchronize.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string[] tables, SyncType syncType, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default) =>
            this.SynchronizeAsync(SyncOptions.DefaultScopeName, new SyncSetup(tables), syncType, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="tables">Tables list to synchronize.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string[] tables, SyncType syncType, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default) =>
            this.SynchronizeAsync(SyncOptions.DefaultScopeName, new SyncSetup(tables), syncType, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="tables">Tables list to synchronize.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, string[] tables, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default) =>
            this.SynchronizeAsync(scopeName, new SyncSetup(tables), SyncType.Normal, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="tables">Tables list to synchronize.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, string[] tables, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default) =>
            this.SynchronizeAsync(scopeName, new SyncSetup(tables), SyncType.Normal, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="tables">Tables list to synchronize.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, string[] tables, SyncType syncType, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default) =>
            this.SynchronizeAsync(scopeName, new SyncSetup(tables), syncType, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="tables">Tables list to synchronize.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, string[] tables, SyncType syncType, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default) =>
            this.SynchronizeAsync(scopeName, new SyncSetup(tables), syncType, parameters, progress, cancellationToken);

        // ---------------------------------------------
        // SyncSetup setup
        // ---------------------------------------------

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="setup">Setup instance containing the table list and optionnally columns.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(SyncSetup setup, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(SyncOptions.DefaultScopeName, setup, SyncType.Normal, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="setup">Setup instance containing the table list and optionnally columns.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(SyncSetup setup, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(SyncOptions.DefaultScopeName, setup, SyncType.Normal, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="setup">Setup instance containing the table list and optionnally columns.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(SyncSetup setup, SyncType syncType, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(SyncOptions.DefaultScopeName, setup, syncType, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on scope DefaultScope.
        /// </summary>
        /// <param name="setup">Setup instance containing the table list and optionnally columns.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(SyncSetup setup, SyncType syncType, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(SyncOptions.DefaultScopeName, setup, syncType, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="setup">Setup instance containing the table list and optionnally columns.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, SyncSetup setup, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(scopeName, setup, SyncType.Normal, null, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="setup">Setup instance containing the table list and optionnally columns.</param>
        /// <param name="parameters">Parameters values for each of your setup filters.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, SyncSetup setup, SyncParameters parameters, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(scopeName, setup, SyncType.Normal, parameters, progress, cancellationToken);

        /// <summary>
        /// Launch a Synchronization based on a named scope.
        /// </summary>
        /// <param name="scopeName">Named scope.</param>
        /// <param name="setup">Setup instance containing the table list and optionnally columns.</param>
        /// <param name="syncType">Synchronization mode: Normal, Reinitialize or ReinitializeWithUpload.</param>
        /// <param name="progress">IProgress instance to get a progression status during sync.</param>
        /// <returns>Computed sync results.</returns>
        public Task<SyncResult> SynchronizeAsync(string scopeName, SyncSetup setup, SyncType syncType, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.SynchronizeAsync(scopeName, setup, syncType, null, progress, cancellationToken);

        // ---------------------------------------------
    }
}