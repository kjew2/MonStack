using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Api;
using Unity.Services.CloudSave.Model;

namespace MonStackAccount
{
    /// <summary>
    /// <see cref="ICloudSave"/> on UGS Cloud Save through the Cloud Code C# SDK (Com.Unity.Services.CloudCode.Apis): private items
    /// with the module's service token, so only the module reads or writes them (specs/27 section 3.3.1). Not covered by the
    /// in-process tests (they use a fake with the same semantics); the remote run in server/ugs/README.md covers it.
    /// </summary>
    public sealed class UgsCloudSave : ICloudSave
    {
        readonly ICloudSaveDataApi _api;
        readonly IExecutionContext _ctx;

        public UgsCloudSave(ICloudSaveDataApi api, IExecutionContext ctx)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        string Token => _ctx.ServiceToken;
        string Project => _ctx.ProjectId;

        public async Task<IReadOnlyList<SavedItem>> GetAsync(CloudSaveOwner owner, IReadOnlyList<string> keys)
        {
            var list = keys.ToList();
            var found = new List<SavedItem>();
            string after = null;
            for (int guard = 0; guard < 1000; guard++)
            {
                var r = owner.Custom
                    ? await _api.GetPrivateCustomItemsAsync(_ctx, Token, Project, owner.Id, list, after).ConfigureAwait(false)
                    : await _api.GetPrivateItemsAsync(_ctx, Token, Project, owner.Id, list, after).ConfigureAwait(false);
                var page = r.Data?.Results ?? new List<Item>();
                foreach (var it in page) found.Add(new SavedItem(it.Key, Text(it.Value), it.WriteLock));
                if (page.Count == 0 || string.IsNullOrEmpty(r.Data?.Links?.Next)) return found;
                after = page[page.Count - 1].Key;
            }
            throw new InvalidOperationException("Cloud Save kept paging " + owner);
        }

        public async Task<IReadOnlyList<string>> KeysAsync(CloudSaveOwner owner, string after)
        {
            var r = owner.Custom
                ? await _api.GetPrivateCustomKeysAsync(_ctx, Token, Project, owner.Id, after).ConfigureAwait(false)
                : await _api.GetPrivateKeysAsync(_ctx, Token, Project, owner.Id, after).ConfigureAwait(false);
            return (r.Data?.Results ?? new List<KeyMetadata>()).Select(k => k.Key).ToList();
        }

        public async Task<string> SetAsync(CloudSaveOwner owner, string key, string value, string writeLock)
        {
            var body = new SetItemBody(key, value, writeLock);
            try
            {
                var r = owner.Custom
                    ? await _api.SetPrivateCustomItemAsync(_ctx, Token, Project, owner.Id, body).ConfigureAwait(false)
                    : await _api.SetPrivateItemAsync(_ctx, Token, Project, owner.Id, body).ConfigureAwait(false);
                return r.Data?.WriteLock;
            }
            catch (ApiException e) when (writeLock != null && e.Response?.StatusCode == HttpStatusCode.Conflict)
            {
                throw new WriteConflictException(key);
            }
        }

        public async Task SetManyAsync(CloudSaveOwner owner, IReadOnlyDictionary<string, string> items)
        {
            if (items.Count == 0) return;
            var body = new SetItemBatchBody(items.Select(kv => new SetItemBody(kv.Key, kv.Value)).ToList());
            if (owner.Custom) await _api.SetPrivateCustomItemBatchAsync(_ctx, Token, Project, owner.Id, body).ConfigureAwait(false);
            else await _api.SetPrivateItemBatchAsync(_ctx, Token, Project, owner.Id, body).ConfigureAwait(false);
        }

        public async Task DeleteAsync(CloudSaveOwner owner, string key)
        {
            try
            {
                if (owner.Custom) await _api.DeletePrivateCustomItemAsync(_ctx, Token, Project, owner.Id, key).ConfigureAwait(false);
                else await _api.DeletePrivateItemAsync(_ctx, Token, Project, owner.Id, key).ConfigureAwait(false);
            }
            catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound) { }
        }

        /// <summary>Items are stored as JSON strings; the SDK hands the value back as a string or a JSON token.</summary>
        static string Text(object value)
        {
            switch (value)
            {
                case null: return null;
                case string s: return s;
                case JValue v when v.Type == JTokenType.String: return (string)v;
                case JToken t: return t.ToString(Formatting.None);
                default: return JsonConvert.SerializeObject(value);
            }
        }
    }
}
