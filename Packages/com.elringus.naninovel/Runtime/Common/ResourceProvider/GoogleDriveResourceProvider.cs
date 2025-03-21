#if UNITY_GOOGLE_DRIVE_AVAILABLE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using UnityEngine;
using UnityGoogleDrive;

namespace Naninovel
{
    /// <summary>
    /// Provides resources stored in Google Drive.
    /// Will only work for the resources covered by the available converters; 
    /// use <see cref="AddConverter{T}(IRawConverter{T})"/> to extend covered resource types.
    /// </summary>
    public class GoogleDriveResourceProvider : ResourceProvider
    {
        [Serializable]
        public class CacheManifest : SerializableLiteralStringMap
        {
            public virtual string StartToken { get => ContainsKey(startTokenKey) ? this[startTokenKey] : null; set => this[startTokenKey] = value; }

            private const string startTokenKey = "GDRIVE_CACHE_START_TOKEN";
            private static readonly string filePath = string.Concat(CacheDirPath, "/CacheManifest");

            public static async UniTask<CacheManifest> ReadOrCreate ()
            {
                if (!File.Exists(filePath))
                {
                    var manifest = new CacheManifest();
                    await IOUtils.WriteTextFile(filePath, JsonUtility.ToJson(manifest));
                    return manifest;
                }

                var manifestJson = await IOUtils.ReadTextFile(filePath);
                return JsonUtility.FromJson<CacheManifest>(manifestJson);
            }

            public virtual async UniTask Write ()
            {
                var manifestJson = JsonUtility.ToJson(this);
                await IOUtils.WriteTextFile(filePath, manifestJson);
            }
        }

        public enum CachingPolicyType
        {
            Smart,
            PurgeAllOnInit
        }

        /// <summary>
        /// Full path to the cache directory.
        /// </summary>
        public static readonly string CacheDirPath = string.Concat(Application.persistentDataPath, "/GoogleDriveResourceProviderCache");
        /// <summary>
        /// String used to replace slashes in file paths.
        /// </summary>
        public const string SlashReplace = "@@";

        /// <summary>
        /// Path to the drive folder where resources are located.
        /// </summary>
        public virtual string DriveRootPath { get; }
        /// <summary>
        /// Limits concurrent requests count using queueing.
        /// </summary>
        public virtual int ConcurrentRequestsLimit { get; }
        /// <summary>
        /// Caching policy to use.
        /// </summary>
        public CachingPolicyType CachingPolicy { get; }
        /// <summary>
        /// Current pending concurrent requests count.
        /// </summary>
        public int RequestsCount => LoadRunners.Count + LocateRunners.Count;

        private readonly Dictionary<Type, IConverter> converters = new Dictionary<Type, IConverter>();
        private readonly Queue<Action> requestQueue = new Queue<Action>();
        private bool smartCachingScanPending;

        public GoogleDriveResourceProvider (string driveRootPath, CachingPolicyType cachingPolicy, int concurrentRequestsLimit)
        {
            DriveRootPath = driveRootPath;
            CachingPolicy = cachingPolicy;
            ConcurrentRequestsLimit = concurrentRequestsLimit;

            IOUtils.CreateDirectory(CacheDirPath);

            if (CachingPolicy == CachingPolicyType.PurgeAllOnInit) PurgeCache();
            if (CachingPolicy == CachingPolicyType.Smart) smartCachingScanPending = true;
        }

        public override bool SupportsType<T> () => converters.ContainsKey(typeof(T));

        /// <summary>
        /// Adds a resource type converter.
        /// </summary>
        public virtual void AddConverter<T> (IRawConverter<T> converter)
        {
            if (converters.ContainsKey(typeof(T))) return;
            converters.Add(typeof(T), converter);
            LogMessage($"Converter '{typeof(T).Name}' added.");
        }

        public void PurgeCache ()
        {
            if (Directory.Exists(CacheDirPath))
            {
                IOUtils.DeleteDirectory(CacheDirPath, true);
                IOUtils.CreateDirectory(CacheDirPath);
            }

            LogMessage("All cached resources purged.");
        }

        public virtual void PurgeCachedResources (string resourcesPath)
        {
            if (!Directory.Exists(CacheDirPath)) return;

            resourcesPath = resourcesPath.Replace("/", SlashReplace) + SlashReplace;

            foreach (var filePath in Directory.GetFiles(CacheDirPath).Where(f => Path.GetFileName(f).StartsWith(resourcesPath)))
            {
                File.Delete(filePath);
                LogMessage($"Cached resource '{filePath}' purged.");
            }

            IOUtils.WebGLSyncFs();
        }

        public override async UniTask<Resource<T>> LoadResource<T> (string path)
        {
            if (smartCachingScanPending) await RunSmartCachingScan();
            return await base.LoadResource<T>(path);
        }

        protected override void RunResourceLoader<T> (LoadResourceRunner<T> loader)
        {
            if (ConcurrentRequestsLimit > 0 && RequestsCount > ConcurrentRequestsLimit)
                requestQueue.Enqueue(() => loader.Run().Forget());
            else base.RunResourceLoader(loader);
        }

        protected override void RunResourcesLocator<T> (LocateResourcesRunner<T> locator)
        {
            if (ConcurrentRequestsLimit > 0 && RequestsCount > ConcurrentRequestsLimit)
                requestQueue.Enqueue(() => locator.Run().Forget());
            else base.RunResourcesLocator(locator);
        }

        protected override LoadResourceRunner<T> CreateLoadResourceRunner<T> (string path)
        {
            return new GoogleDriveResourceLoader<T>(this, DriveRootPath, path, ResolveConverter<T>(), LogMessage);
        }

        protected override LocateResourcesRunner<T> CreateLocateResourcesRunner<T> (string path)
        {
            return new GoogleDriveResourceLocator<T>(this, DriveRootPath, path, ResolveConverter<T>());
        }

        protected override LocateFoldersRunner CreateLocateFoldersRunner (string path)
        {
            return new GoogleDriveFolderLocator(this, DriveRootPath, path);
        }

        protected override void HandleResourceLoaded<T> (Resource<T> resource)
        {
            base.HandleResourceLoaded(resource);
            ProcessLoadQueue();
        }

        protected override void HandleResourcesLocated<T> (IReadOnlyCollection<string> locatedResourcePaths, string path)
        {
            base.HandleResourcesLocated<T>(locatedResourcePaths, path);
            ProcessLoadQueue();
        }

        protected override void DisposeResource (Resource resource)
        {
            if (!resource.Valid) return;
            ObjectUtils.DestroyOrImmediate(resource.Object);
        }

        protected virtual IRawConverter<T> ResolveConverter<T> ()
        {
            var resourceType = typeof(T);
            if (!converters.TryGetValue(resourceType, out var converter))
                throw new Error($"Converter for resource of type '{resourceType.Name}' is not available.");
            return converter as IRawConverter<T>;
        }

        protected virtual void ProcessLoadQueue ()
        {
            if (requestQueue.Count == 0) return;

            requestQueue.Dequeue()();
        }

        protected virtual async UniTask RunSmartCachingScan ()
        {
            smartCachingScanPending = false;

            var startTime = DateTime.Now;
            var manifest = await CacheManifest.ReadOrCreate();
            LogMessage("Running smart caching scan...");

            if (!string.IsNullOrEmpty(manifest.StartToken))
                await ProcessChangesList(manifest);

            var newStartToken = (await SendRequest(GoogleDriveChanges.GetStartPageToken())).StartPageTokenValue;
            manifest.StartToken = newStartToken;
            await manifest.Write();
            LogMessage($"Updated smart cache changes token: {newStartToken}");
            LogMessage($"Finished smart caching scan in {(DateTime.Now - startTime).TotalSeconds:0.###} seconds.");
        }

        protected virtual async UniTask ProcessChangesList (CacheManifest manifest)
        {
            var changeList = await SendRequest(GoogleDriveChanges.List(manifest.StartToken));
            foreach (var change in changeList.Changes)
            {
                if (!manifest.ContainsKey(change.FileId)) continue;

                var filePath = string.Concat(CacheDirPath, "/", manifest[change.FileId]);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    LogMessage($"File '{filePath}' has been changed; cached version has been purged.");
                }
            }

            if (!string.IsNullOrWhiteSpace(changeList.NextPageToken))
            {
                manifest.StartToken = changeList.NextPageToken;
                await ProcessChangesList(manifest);
            }

            IOUtils.WebGLSyncFs();
        }

        protected virtual async UniTask<T> SendRequest<T> (GoogleDriveRequest<T> request)
        {
            await request.Send();
            if (request.IsError) throw new WebException(request.Error);
            return request.ResponseData;
        }
    }
}

#endif
