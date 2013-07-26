using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SymbolSource.Server.Management.Client;
using Version = SymbolSource.Server.Management.Client.Version;
using System.Linq.Expressions;  

namespace SymbolSource.Server.Basic
{
	public partial class BasicBackend {
        
        #region PackageCacheDictionary

        /// <summary>
        /// A thread-safe cache of package queries
        /// </summary>
        private class PackageCache {

            #region CacheValidationRules

            /// <summary>
            /// 
            /// </summary>
            public class CacheValidationRules {
                /// <summary>
                /// Specifies the file search pattern to be used to filter the files that will be examined for changes.
                /// Default is *.nupkg.
                /// </summary>
                public string CheckFileSearchPattern = "*.nupkg";
                /// <summary>
                /// If true the cache will be invalidated when the total number of filtered files changes
                /// </summary>
                public bool CheckFileCount = true;
                /// <summary>
                /// If true the cache will be invalidated when the modified date of any filtered file changes
                /// </summary>
                public bool CheckFileModifiedDate = false;
                /// <summary>
                /// When true then the cache is enabled.
                /// </summary>
                public bool Enabled = true;
            }

            #endregion

            private CacheValidationRules _validationRules = new CacheValidationRules();
            private Version[] _versions = null;
            private object _cacheLock = new object();
            private int _fileCount;
            private DateTime _fileLastModified;
            private string _dataDirectory;

            /// <summary>
            /// Initialises a new instance of the <see cref="PackageCache"/> class
            /// to the specified <paramref name="dataDirectory"/>
            /// </summary>
            /// <param name="dataDirectory"></param>
            public PackageCache(string dataDirectory) {
                _dataDirectory = dataDirectory;
                this.ResetCache();
            }

            /// <summary>
            /// Gets the Validation Rules used to check the cache
            /// </summary>
            public CacheValidationRules ValidationRules { get { return _validationRules; } }

            /// <summary>
            /// Validates the cache, and returns the cached versions.
            /// </summary>
            /// <returns></returns>
            public Version[] GetCachedVersions() {
                ValidateCache();
                return _versions;
            }
            /// <summary>
            /// Caches the versions
            /// </summary>
            /// <param name="versions"></param>
            public void SetCachedVersions(Version[] versions) {
                lock (_cacheLock) {
                    _versions = versions;
                }
            }

            private void ValidateCache() {

                // If the data directory is invalid, or force invalidation is enabled, then reset the cache
                if (string.IsNullOrEmpty(_dataDirectory) || 
                    !Directory.Exists(_dataDirectory) ||
                    !this.ValidationRules.Enabled) {
                    this.ResetCache();
                    return;
                }
                
                // File-based checks
                if (this.ValidationRules.CheckFileCount || this.ValidationRules.CheckFileModifiedDate) {

                    DateTime fileLastModified = DateTime.MinValue;
                    int fileCount = -1;

                    // Get the *.nupkg files in the data directory
                    string[] files = Directory.GetFiles(
                        _dataDirectory, 
                        this.ValidationRules.CheckFileSearchPattern, 
                        SearchOption.AllDirectories);

                    // Check the file count
                    if (this.ValidationRules.CheckFileCount) {
                        fileCount = files.Length;
                    }
                    // Check the modified date/time of each file - this takes alot longer
                    // and isn't required provided as pushing new packages will change the file count.
                    if (this.ValidationRules.CheckFileModifiedDate) {
                        foreach (string file in files) {
                            DateTime fileLastWriteTime = File.GetLastWriteTimeUtc(file);
                            if (fileLastWriteTime > fileLastModified) fileLastModified = fileLastWriteTime;
                        }
                    }
                    // If there is a change, reset the cache
                    if (_fileCount != fileCount || _fileLastModified != fileLastModified) {
                        this.ResetCache(fileCount, fileLastModified);
                        return;
                    }
                }
            }

            private void ResetCache() {
                this.ResetCache(0, DateTime.MinValue);
            }
            private void ResetCache(int fileCount, DateTime fileLastModified) {
                lock (_cacheLock) {
                    _versions = null;
                    _fileCount = fileCount;
                    _fileLastModified = fileLastModified;
                }
            }

        }

        #endregion
       
        private static PackageCache _packageCache = null;
        private PackageCache CurrentPackageCache {
            get {
                if (_packageCache == null) {
                    // Create the singleton instance
                    lock(this) {
                        _packageCache = new PackageCache(configuration.DataPath);
                    }
                }
                return _packageCache;
            }
        }

        private Version[] GetPackagesFromCache(ref Repository repository, ref PackageFilter filter, string packageFormat, string projectId) {

            // Try the cache first
            var versions = this.CurrentPackageCache.GetCachedVersions();
            if (versions != null) return versions; // Return the result if non-null, i.e. an entry was found

            // Not in the cache, so build the version from the full repository
            // using the original code
            versions = this.GetPackagesInternal(ref repository, ref filter, packageFormat, projectId);

            // Add to the cache
            this.CurrentPackageCache.SetCachedVersions(versions);

            // Return the result
            return versions;
        }

	}
}
