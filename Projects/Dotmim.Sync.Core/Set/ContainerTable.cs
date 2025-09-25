using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Dotmim.Sync
{
    /// <summary>
    /// Represents the operation types for batching optimization.
    /// </summary>
    public enum BatchOperationType
    {
        /// <summary>Insert operation</summary>
        Insert,
        /// <summary>Update operation</summary>
        Update,
        /// <summary>Delete operation</summary>
        Delete
    }

    /// <summary>
    /// ContainerTable is a table with columns and rows to be sent over the wire.
    /// </summary>
    [DataContract(Name = "ct"), Serializable]
    public class ContainerTable : SyncNamedItem<ContainerTable>
    {
        private int? _batchingColumnIndex;

        /// <summary>
        /// Gets or sets the name of the table that the DmTableSurrogate object represents.
        /// </summary>
        [DataMember(Name = "n", IsRequired = true, Order = 1)]
        public string TableName { get; set; }

        /// <summary>
        /// Gets or sets get or Set the schema used for the DmTableSurrogate.
        /// </summary>
        [DataMember(Name = "s", IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public string SchemaName { get; set; }

        /// <summary>
        /// Gets or sets get or Set the columns name used for the DmTableSurrogate.
        /// </summary>
        [DataMember(Name = "c", IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public List<ContainerTableColum> Columns { get; set; }

        /// <summary>
        /// Gets or sets list of rows.
        /// </summary>
        [DataMember(Name = "r", IsRequired = false, Order = 4)]

        // [JsonConverter(typeof(ArrayJsonConverter))]
        public List<object[]> Rows { get; set; } = new List<object[]>();

        /// <inheritdoc cref="ContainerTable"/>
        public ContainerTable()
        {
        }

        /// <inheritdoc cref="ContainerTable"/>
        public ContainerTable(SyncTable table)
        {
            this.TableName = table.TableName;
            this.SchemaName = table.SchemaName;
            this.Columns = [];
            foreach (var column in table.Columns)
            {
                this.Columns.Add(new ContainerTableColum { ColumnName = column.ColumnName, TypeName = column.DataType, IsPrimaryKey = table.IsPrimaryKey(column) ? 1 : null });
            }
        }

        /// <inheritdoc cref="ContainerTable"/>
        public ContainerTable(SyncTable table, bool includeBatchingColumn)
        {
            this.TableName = table.TableName;
            this.SchemaName = table.SchemaName;
            this.Columns = [];

            foreach (var column in table.Columns)
            {
                this.Columns.Add(new ContainerTableColum { ColumnName = column.ColumnName, TypeName = column.DataType, IsPrimaryKey = table.IsPrimaryKey(column) ? 1 : null });
            }

            // Add the operation type column for batching optimization
            if (includeBatchingColumn)
            {
                this.Columns.Add(new ContainerTableColum { ColumnName = "_rs", TypeName = "String" });
            }
        }

        /// <summary>
        /// Gets a value indicating whether check if we have rows in this container table.
        /// </summary>
        public bool HasRows => this.Rows.Count > 0;

        /// <summary>
        /// Clear all rows in the container table.
        /// </summary>
        public void Clear() => this.Rows.Clear();

        /// <summary>
        /// Gets a value indicating whether this container table includes the batching operation column (_rs).
        /// </summary>
        public bool HasBatchingColumn => this.BatchingColumnIndex >= 0;

        /// <summary>
        /// Gets the index of the batching operation column (_rs).
        /// </summary>
        public int BatchingColumnIndex
        {
            get
            {
                if (!_batchingColumnIndex.HasValue)
                {
                    _batchingColumnIndex = this.Columns?.FindIndex(c => c.ColumnName == "_rs") ?? -1;
                }
                return _batchingColumnIndex.Value;
            }
        }

        /// <summary>
        /// Converts BatchOperationType enum to string representation.
        /// </summary>
        /// <param name="operationType">The operation type</param>
        /// <returns>String representation: "i", "u", or "d"</returns>
        public static string OperationTypeToString(BatchOperationType operationType)
        {
            return operationType switch
            {
                BatchOperationType.Insert => "i",
                BatchOperationType.Update => "u",
                BatchOperationType.Delete => "d",
                _ => throw new ArgumentOutOfRangeException(nameof(operationType))
            };
        }

        /// <summary>
        /// Converts string representation to BatchOperationType enum.
        /// </summary>
        /// <param name="operationType">String representation: "i", "u", or "d"</param>
        /// <returns>The corresponding BatchOperationType</returns>
        public static BatchOperationType StringToOperationType(string operationType)
        {
            return operationType switch
            {
                "i" => BatchOperationType.Insert,
                "u" => BatchOperationType.Update,
                "d" => BatchOperationType.Delete,
                _ => throw new ArgumentException($"Invalid operation type: {operationType}")
            };
        }

        /// <summary>
        /// Adds a row with the specified operation type.
        /// </summary>
        /// <param name="rowData">The row data without the operation type</param>
        /// <param name="operationType">The operation type</param>
        public void AddRowWithOperationType(object[] rowData, BatchOperationType operationType)
        {
            if (!HasBatchingColumn)
                throw new InvalidOperationException("Container table must be created with includeBatchingColumn=true to use this method");

            var extendedRow = new object[rowData.Length + 1];
            Array.Copy(rowData, extendedRow, rowData.Length);
            extendedRow[rowData.Length] = OperationTypeToString(operationType); // _rs column is always last
            this.Rows.Add(extendedRow);
        }

        /// <summary>
        /// Gets the operation type for a specific row.
        /// </summary>
        /// <param name="rowIndex">The row index</param>
        /// <returns>The operation type</returns>
        public BatchOperationType GetRowOperationType(int rowIndex)
        {
            if (!HasBatchingColumn)
                throw new InvalidOperationException("Container table does not have batching column");

            if (rowIndex < 0 || rowIndex >= this.Rows.Count)
                throw new ArgumentOutOfRangeException(nameof(rowIndex));

            var row = this.Rows[rowIndex];
            var operationTypeString = row[row.Length - 1]?.ToString(); // _rs column is always last
            return StringToOperationType(operationTypeString);
        }

        /// <inheritdoc cref="SyncNamedItem{T}.GetAllNamesProperties"/>
        public override IEnumerable<string> GetAllNamesProperties()
        {
            yield return this.TableName;
            yield return this.SchemaName;
        }
    }

    /// <summary>
    /// Represents a column in a container table.
    /// </summary>
    [DataContract(Name = "c"), Serializable]
    public class ContainerTableColum
    {
        /// <summary>
        /// Gets or sets the name of the column.
        /// </summary>
        [DataMember(Name = "n", IsRequired = true, Order = 1)]
        public string ColumnName { get; set; }

        /// <summary>
        /// Gets or sets the type of the column.
        /// </summary>
        [DataMember(Name = "t", IsRequired = true, Order = 2)]
        public string TypeName { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the column is a primary key.
        /// </summary>
        [DataMember(Name = "p", IsRequired = false, Order = 3, EmitDefaultValue = false)]
        public byte? IsPrimaryKey { get; set; }

        /// <inheritdoc cref="ContainerTableColum"/>
        public ContainerTableColum()
        {
        }
    }
}