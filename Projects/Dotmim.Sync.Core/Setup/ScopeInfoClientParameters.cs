using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;

namespace Wormhole.Sync
{
    /// <summary>
    /// Represents a collection of ScopeInfoClientParameter.
    /// </summary>
    [CollectionDataContract(Name = "sicps", ItemName = "sicp"), Serializable]
    public class ScopeInfoClientParameters : ObservableCollection<ScopeInfoClientParameter>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeInfoClientParameters"/> class.
        /// </summary>
        public ScopeInfoClientParameters()
        {
        }

        /// <summary>
        /// Add a new parameter to the collection.
        /// </summary>
        public void Add(string name, System.Data.DbType dbType, int maxLength = -1)
        {
            var parameter = new ScopeInfoClientParameter(name, dbType, maxLength);
            this.Add(parameter);
        }

        /// <summary>
        /// Gets a parameter by name (case insensitive).
        /// </summary>
        public ScopeInfoClientParameter this[string name]
        {
            get
            {
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentNullException(nameof(name));

                return this.FirstOrDefault(p => string.Equals(p.Name, name, SyncGlobalization.DataSourceStringComparison));
            }
        }

        /// <summary>
        /// Clone the collection.
        /// </summary>
        public ScopeInfoClientParameters Clone()
        {
            var clone = new ScopeInfoClientParameters();
            foreach (var parameter in this)
                clone.Add(parameter.Clone());
            return clone;
        }

        /// <summary>
        /// Compare two collections.
        /// </summary>
        public bool CompareWith(ScopeInfoClientParameters other)
        {
            if (other == null)
                return false;

            if (this.Count != other.Count)
                return false;

            foreach (var parameter in this)
            {
                var otherParameter = other[parameter.Name];
                if (otherParameter == null || !parameter.EqualsByProperties(otherParameter))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns a string that represents the current collection.
        /// </summary>
        public override string ToString() => $"{this.Count} parameters";
    }
}
