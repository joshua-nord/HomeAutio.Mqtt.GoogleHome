﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdentityServer4.Models;
using IdentityServer4.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HomeAutio.Mqtt.GoogleHome.Identity
{
    /// <summary>
    /// Persisted grant store.
    /// </summary>
    public class PersistedGrantStore : IPersistedGrantStoreWithExpiration
    {
        private static readonly object _readLock = new object();
        private static readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        private readonly ILogger<PersistedGrantStore> _log;
        private readonly ConcurrentDictionary<string, PersistedGrant> _repository = new ConcurrentDictionary<string, PersistedGrant>();
        private readonly string _file;

        // Explicitly use the default contract resolver to force exact property serialization Base64 keys as they are case sensitive
        private readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings { ContractResolver = new DefaultContractResolver() };

        /// <summary>
        /// Initializes a new instance of the <see cref="PersistedGrantStore"/> class.
        /// </summary>
        /// <param name="logger">Logging instance.</param>
        /// <param name="configuration">Conffguration.</param>
        public PersistedGrantStore(ILogger<PersistedGrantStore> logger, IConfiguration configuration)
        {
            _log = logger ?? throw new ArgumentException(nameof(logger));
            if (configuration == null)
                throw new ArgumentException(nameof(configuration));

            _file = configuration.GetValue<string>("oauth:tokenStoreFile");
            RestoreFromFile();
        }

        /// <inheritdoc />
        public async Task StoreAsync(PersistedGrant grant)
        {
            _repository[grant.Key] = grant;

            await WriteToFileAsync();
        }

        /// <inheritdoc />
        public Task<PersistedGrant> GetAsync(string key)
        {
            if (_repository.TryGetValue(key, out PersistedGrant token))
            {
                return Task.FromResult(token);
            }

            _log.LogWarning("Failed to find token with key {key}", key);
            return Task.FromResult<PersistedGrant>(null);
        }

        /// <inheritdoc />
        public Task<IEnumerable<PersistedGrant>> GetAllAsync(PersistedGrantFilter filter)
        {
            var query = _repository.AsEnumerable();

            if (!string.IsNullOrEmpty(filter.ClientId))
                query = query.Where(x => x.Value.ClientId == filter.ClientId);

            if (!string.IsNullOrEmpty(filter.SubjectId))
                query = query.Where(x => x.Value.SubjectId == filter.SubjectId);

            if (!string.IsNullOrEmpty(filter.SessionId))
                query = query.Where(x => x.Value.SessionId == filter.SessionId);

            var items = query.Select(x => x.Value).AsEnumerable();
            return Task.FromResult(items);
        }

        /// <inheritdoc />
        public async Task RemoveAsync(string key)
        {
            if (_repository.TryRemove(key, out _))
            {
                await WriteToFileAsync();
            }
            else
            {
                _log.LogWarning("Failed to remove token with key {key}", key);
            }
        }

        /// <inheritdoc />
        public async Task RemoveAllAsync(PersistedGrantFilter filter)
        {
            var query = _repository.AsEnumerable();

            if (!string.IsNullOrEmpty(filter.ClientId))
                query = query.Where(x => x.Value.ClientId == filter.ClientId);

            if (!string.IsNullOrEmpty(filter.SubjectId))
                query = query.Where(x => x.Value.SubjectId == filter.SubjectId);

            if (!string.IsNullOrEmpty(filter.SessionId))
                query = query.Where(x => x.Value.SessionId == filter.SessionId);

            var keys = query.Select(x => x.Key);
            var numKeysRemoved = 0;
            foreach (var key in keys)
            {
                if (_repository.TryRemove(key, out _))
                    numKeysRemoved++;
                else
                    _log.LogWarning("Failed to remove token with key {key}", key);
            }

            if (numKeysRemoved > 0)
                await WriteToFileAsync();
        }

        /// <inheritdoc />
        public async Task RemoveAllExpiredAsync()
        {
            var refreshTokenCutoff = DateTime.Now.AddSeconds(-30);
            var query = _repository
                .Where(x => x.Value.Expiration < DateTime.UtcNow || (x.Value.ConsumedTime != null && x.Value.ConsumedTime.Value < refreshTokenCutoff))
                .Select(x => x.Key);

            var keys = query.ToArray();
            var numKeysRemoved = 0;
            foreach (var key in keys)
            {
                if (_repository.TryRemove(key, out _))
                    numKeysRemoved++;
                else
                    _log.LogWarning("Failed to remove token with key {key}", key);
            }

            if (numKeysRemoved > 0)
                await WriteToFileAsync();
        }

        /// <summary>
        /// Initialize current state from file.
        /// </summary>
        private void RestoreFromFile()
        {
            if (File.Exists(_file))
            {
                lock (_readLock)
                {
                    var fileContents = File.ReadAllText(_file);
                    if (string.IsNullOrEmpty(fileContents))
                    {
                        _log.LogWarning("Token file {file} already exists but is empty", _file);
                        return;
                    }

                    var deserializedFileContents = JsonConvert.DeserializeObject<Dictionary<string, PersistedGrant>>(fileContents, _jsonSerializerSettings);

                    _repository.Clear();
                    foreach (var record in deserializedFileContents)
                    {
                        if (!_repository.TryAdd(record.Key, record.Value))
                        {
                            _log.LogWarning("Failed to restore token with key {key}", record.Key);
                        }
                    }

                    _log.LogInformation("Restored tokens from {file}", _file);
                }
            }
        }

        /// <summary>
        /// Write the current state to file.
        /// </summary>
        /// <returns>An awaitable <see cref="Task"/>.</returns>
        private async Task WriteToFileAsync()
        {
            _log.LogInformation("Writing tokens to {file}", _file);

            await _semaphoreSlim.WaitAsync();

            try
            {
                var contents = JsonConvert.SerializeObject(_repository, _jsonSerializerSettings);
                await File.WriteAllTextAsync(_file, contents);

                _log.LogInformation("Wrote tokens to {file}", _file);
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
    }
}
