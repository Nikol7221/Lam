using Lampac.Engine;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.SQL;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Lampac.Controllers
{
    public class BookmarkController : BaseController
    {
        #region bookmark.js
        [HttpGet]
        [Route("bookmark.js")]
        [Route("bookmark/js/{token}")]
        public ActionResult BookmarkJS(string token)
        {
            if (!AppInit.conf.storage.enable)
                return Content(string.Empty, "application/javascript; charset=utf-8");

            var sb = new StringBuilder(FileCache.ReadAllText("plugins/bookmark.js"));

            sb.Replace("{localhost}", host)
              .Replace("{token}", HttpUtility.UrlEncode(token));

            return Content(sb.ToString(), "application/javascript; charset=utf-8");
        }
        #endregion

        static readonly string[] BookmarkCategories = {
            "history",
            "like",
            "watch",
            "wath",
            "book",
            "look",
            "viewed",
            "scheduled",
            "continued",
            "thrown"
        };

        #region List
        [HttpGet]
        [Route("/bookmark/list")]
        public async Task<ActionResult> List(string filed)
        {
            #region migration storage to sql
            if (AppInit.conf.syncBeta && !string.IsNullOrEmpty(requestInfo.user_uid))
            {
                string profile_id = getProfileid(requestInfo, HttpContext);
                string id = requestInfo.user_uid + profile_id;

                string md5key = AppInit.conf.storage.md5name ? CrypTo.md5(id) : Regex.Replace(id, "(\\@|_)", "");
                string storageFile = $"database/storage/sync_favorite/{md5key.Substring(0, 2)}/{md5key.Substring(2)}";

                if (System.IO.File.Exists(storageFile) && !System.IO.File.Exists($"{storageFile}.migration"))
                {
                    string semaphoreKey = $"BookmarkController:{getUserid(requestInfo, HttpContext)}";
                    var semaphore = _semaphoreLocks.GetOrAdd(semaphoreKey, _ => new SemaphoreSlim(1, 1));

                    try
                    {
                        await semaphore.WaitAsync(TimeSpan.FromSeconds(40));

                        if (System.IO.File.Exists(storageFile) && !System.IO.File.Exists($"{storageFile}.migration"))
                        {
                            var content = System.IO.File.ReadAllText(storageFile);
                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                var root = JsonConvert.DeserializeObject<JObject>(content);
                                if (root != null)
                                {
                                    // older format may wrap data under "favorite"; support both
                                    var favorite = root["favorite"] as JObject ?? root;

                                    using (var sqlDb = new SyncUserContext())
                                    {
                                        var (entity, loaded) = LoadBookmarks(sqlDb, getUserid(requestInfo, HttpContext), createIfMissing: true);
                                        bool changed = false;

                                        EnsureDefaultArrays(loaded);

                                        #region migrate card objects
                                        if (favorite["card"] is JArray srcCards)
                                        {
                                            foreach (var c in srcCards.Children<JObject>().ToList())
                                            {
                                                var idToken = c?["id"];
                                                if (idToken != null)
                                                {
                                                    if (long.TryParse(idToken.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cid))
                                                    {
                                                        changed |= EnsureCard(loaded, c, cid, insert: false);
                                                    }
                                                }
                                            }
                                        }
                                        #endregion

                                        #region migrate categories
                                        foreach (var prop in favorite.Properties())
                                        {
                                            var name = prop.Name;

                                            if (string.Equals(name, "card", StringComparison.OrdinalIgnoreCase))
                                                continue;

                                            var srcValue = prop.Value;

                                            if (BookmarkCategories.Contains(name))
                                            {
                                                if (srcValue is JArray srcArray)
                                                {
                                                    var dest = GetCategoryArray(loaded, name);
                                                    foreach (var t in srcArray)
                                                    {
                                                        var idStr = t?.ToString();
                                                        if (string.IsNullOrWhiteSpace(idStr))
                                                            continue;

                                                        if (long.TryParse(idStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var idVal))
                                                        {
                                                            bool exists = dest.Any(dt => dt.ToString() == idVal.ToString(CultureInfo.InvariantCulture));
                                                            if (!exists)
                                                            {
                                                                dest.Add(idVal);
                                                                changed = true;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                var existing = loaded[name];
                                                if (existing == null || !JToken.DeepEquals(existing, srcValue))
                                                {
                                                    loaded[name] = srcValue.DeepClone();
                                                    changed = true;
                                                }
                                            }
                                        }
                                        #endregion

                                        if (changed)
                                            Save(sqlDb, entity, loaded);
                                    }

                                    System.IO.File.Create($"{storageFile}.migration");
                                }
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        try
                        {
                            semaphore.Release();
                        }
                        finally
                        {
                            if (semaphore.CurrentCount == 1)
                                _semaphoreLocks.TryRemove(semaphoreKey, out _);
                        }
                    }
                }
            }
            #endregion

            var data = GetBookmarksForResponse(SyncUserDb.Read);
            if (!string.IsNullOrEmpty(filed))
                return ContentTo(data[filed].ToString(Formatting.None));

            return ContentTo(data.ToString(Formatting.None));
        }
        #endregion

        #region Set
        [HttpPost]
        [Route("/bookmark/set")]
        public async Task<ActionResult> Set(string connectionId)
        {
            if (string.IsNullOrEmpty(requestInfo.user_uid))
                return JsonFailure();

            string body = null;

            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(body))
                return JsonFailure();

            JObject job = null;

            try
            {
                job = JsonConvert.DeserializeObject<JObject>(body);
            }
            catch
            {
                return JsonFailure();
            }

            if (job == null)
                return JsonFailure();

            string where = job.Value<string>("where");
            if (string.IsNullOrWhiteSpace(where))
                return JsonFailure();

            where = where.Trim();
            string normalized = where.ToLowerInvariant();

            if (normalized == "card" || BookmarkCategories.Contains(normalized))
                return JsonFailure();

            var valueToken = job.TryGetValue("data", out var token)
                ? token?.DeepClone()
                : null;

            if (valueToken == null)
                return JsonFailure();

            string semaphoreKey = $"BookmarkController:{getUserid(requestInfo, HttpContext)}";
            var semaphore = _semaphoreLocks.GetOrAdd(semaphoreKey, _ => new SemaphoreSlim(1, 1));

            try
            {
                await semaphore.WaitAsync(TimeSpan.FromSeconds(40));

                using (var sqlDb = new SyncUserContext())
                {
                    var (entity, data) = LoadBookmarks(sqlDb, getUserid(requestInfo, HttpContext), createIfMissing: true);

                    data[where] = valueToken;

                    EnsureDefaultArrays(data);

                    Save(sqlDb, entity, data);
                }

                string edata = JsonConvert.SerializeObject(new { type = "set", where, data = valueToken, profile_id = getProfileid(requestInfo, HttpContext) });
                _ = nws.SendEvents(connectionId, requestInfo.user_uid, "bookmark", edata).ConfigureAwait(false);

                return JsonSuccess();
            }
            finally
            {
                try
                {
                    semaphore.Release();
                }
                finally
                {
                    if (semaphore.CurrentCount == 1)
                        _semaphoreLocks.TryRemove(semaphoreKey, out _);
                }
            }
        }

        #endregion

        #region Add/Added
        [HttpPost]
        [Route("/bookmark/add")]
        [Route("/bookmark/added")]
        public async Task<ActionResult> Add(string connectionId)
        {
            if (string.IsNullOrEmpty(requestInfo.user_uid))
                return JsonFailure();

            var readBody = await ReadPayloadAsync();

            var payload = readBody.payload;
            if (payload?.Card == null)
                return JsonFailure();

            var cardId = payload.ResolveCardId();
            if (cardId == null)
                return JsonFailure();

            string category = NormalizeCategory(payload.Where);

            string semaphoreKey = $"BookmarkController:{getUserid(requestInfo, HttpContext)}";
            var semaphore = _semaphoreLocks.GetOrAdd(semaphoreKey, _ => new SemaphoreSlim(1, 1));

            bool isAddedRequest = HttpContext?.Request?.Path.Value?.StartsWith("/bookmark/added", StringComparison.OrdinalIgnoreCase) == true;

            try
            {
                await semaphore.WaitAsync(TimeSpan.FromSeconds(40));

                using (var sqlDb = new SyncUserContext())
                {
                    var (entity, data) = LoadBookmarks(sqlDb, getUserid(requestInfo, HttpContext), createIfMissing: true);
                    bool changed = false;

                    changed |= EnsureCard(data, payload.Card, cardId.Value);

                    if (!string.IsNullOrEmpty(category))
                        changed |= AddToCategory(data, category, cardId.Value);

                    if (isAddedRequest)
                        changed |= MoveIdToFrontInAllCategories(data, cardId.Value);

                    if (changed)
                    {
                        Save(sqlDb, entity, data);

                        if (readBody.json != null)
                        {
                            string edata = JsonConvert.SerializeObject(new 
                            { 
                                type = isAddedRequest ? "added" : "add", 
                                readBody.json, 
                                profile_id = getProfileid(requestInfo, HttpContext) 
                            });

                            _ = nws.SendEvents(connectionId, requestInfo.user_uid, "bookmark", edata).ConfigureAwait(false);
                        }
                    }
                }

                return JsonSuccess();
            }
            finally
            {
                try
                {
                    semaphore.Release();
                }
                finally
                {
                    if (semaphore.CurrentCount == 1)
                        _semaphoreLocks.TryRemove(semaphoreKey, out _);
                }
            }
        }
        #endregion

        #region Remove
        [HttpPost]
        [Route("/bookmark/remove")]
        public async Task<ActionResult> Remove(string connectionId)
        {
            if (string.IsNullOrEmpty(requestInfo.user_uid))
                return JsonFailure();

            var readBody = await ReadPayloadAsync();

            var payload = readBody.payload;
            if (payload == null)
                return JsonFailure();

            var cardId = payload.ResolveCardId();
            if (cardId == null)
                return JsonFailure();

            string category = NormalizeCategory(payload.Where);
            string method = payload.NormalizedMethod;

            string semaphoreKey = $"BookmarkController:{getUserid(requestInfo, HttpContext)}";
            var semaphore = _semaphoreLocks.GetOrAdd(semaphoreKey, _ => new SemaphoreSlim(1, 1));

            try
            {
                await semaphore.WaitAsync(TimeSpan.FromSeconds(40));

                using (var sqlDb = new SyncUserContext())
                {
                    var (entity, data) = LoadBookmarks(sqlDb, getUserid(requestInfo, HttpContext), createIfMissing: false);
                    if (entity == null)
                        return JsonSuccess();

                    bool changed = false;

                    if (!string.IsNullOrEmpty(category))
                        changed |= RemoveFromCategory(data, category, cardId.Value);

                    if (string.Equals(method, "card", StringComparison.Ordinal))
                    {
                        changed |= RemoveIdFromAllCategories(data, cardId.Value);
                        changed |= RemoveCard(data, cardId.Value);
                    }

                    if (changed)
                    {
                        Save(sqlDb, entity, data);

                        if (readBody.json != null)
                        {
                            string edata = JsonConvert.SerializeObject(new { type = "remove", readBody.json, profile_id = getProfileid(requestInfo, HttpContext) });
                            _ = nws.SendEvents(connectionId, requestInfo.user_uid, "bookmark", edata).ConfigureAwait(false);
                        }
                    }
                }

                return JsonSuccess();
            }
            finally
            {
                try
                {
                    semaphore.Release();
                }
                finally
                {
                    if (semaphore.CurrentCount == 1)
                        _semaphoreLocks.TryRemove(semaphoreKey, out _);
                }
            }
        }
        #endregion


        #region static
        static string getUserid(RequestModel requestInfo, HttpContext httpContext)
        {
            string user_id = requestInfo.user_uid;
            string profile_id = getProfileid(requestInfo, httpContext);

            if (!string.IsNullOrEmpty(profile_id))
                return $"{user_id}_{profile_id}";

            return user_id;
        }

        static string getProfileid(RequestModel requestInfo, HttpContext httpContext)
        {
            if (httpContext.Request.Query.TryGetValue("profile_id", out var profile_id) && !string.IsNullOrEmpty(profile_id) && profile_id != "0")
                return profile_id;

            return string.Empty;
        }

        JObject GetBookmarksForResponse(SyncUserContext sqlDb)
        {
            if (string.IsNullOrEmpty(requestInfo.user_uid))
                return CreateDefaultBookmarks();

            string user_id = getUserid(requestInfo, HttpContext);
            var entity = sqlDb.bookmarks.AsNoTracking().FirstOrDefault(i => i.user == user_id);
            var data = entity != null ? DeserializeBookmarks(entity.data) : CreateDefaultBookmarks();
            EnsureDefaultArrays(data);
            return data;
        }

        static (SyncUserBookmarkSqlModel entity, JObject data) LoadBookmarks(SyncUserContext sqlDb, string userUid, bool createIfMissing)
        {
            JObject data = CreateDefaultBookmarks();
            SyncUserBookmarkSqlModel entity = null;

            if (!string.IsNullOrEmpty(userUid))
            {
                entity = sqlDb.bookmarks.FirstOrDefault(i => i.user == userUid);
                if (entity != null && !string.IsNullOrEmpty(entity.data))
                    data = DeserializeBookmarks(entity.data);
            }

            EnsureDefaultArrays(data);

            if (entity == null && createIfMissing && !string.IsNullOrEmpty(userUid))
                entity = new SyncUserBookmarkSqlModel { user = userUid };

            return (entity, data);
        }

        static JObject DeserializeBookmarks(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return CreateDefaultBookmarks();

            try
            {
                var job = JsonConvert.DeserializeObject<JObject>(json) ?? new JObject();
                EnsureDefaultArrays(job);
                return job;
            }
            catch
            {
                return CreateDefaultBookmarks();
            }
        }

        static JObject CreateDefaultBookmarks()
        {
            var obj = new JObject
            {
                ["card"] = new JArray()
            };

            foreach (var category in BookmarkCategories)
                obj[category] = new JArray();

            return obj;
        }

        static void EnsureDefaultArrays(JObject root)
        {
            if (root == null)
                return;

            if (root["card"] is not JArray)
                root["card"] = new JArray();

            foreach (var category in BookmarkCategories)
            {
                if (root[category] is not JArray)
                    root[category] = new JArray();
            }
        }

        static string NormalizeCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category) || category.Trim().ToLower() == "card")
                return null;

            return category.Trim().ToLowerInvariant();
        }

        static bool EnsureCard(JObject data, JObject card, long id, bool insert = true)
        {
            if (data == null || card == null)
                return false;

            var cardArray = GetCardArray(data);
            string idStr = id.ToString(CultureInfo.InvariantCulture);
            var newCard = (JObject)card.DeepClone();

            foreach (var existing in cardArray.Children<JObject>().ToList())
            {
                var token = existing["id"];
                if (token != null && token.ToString() == idStr)
                {
                    if (!JToken.DeepEquals(existing, newCard))
                    {
                        existing.Replace(newCard);
                        return true;
                    }

                    return false;
                }
            }

            if (insert)
                cardArray.Insert(0, newCard);
            else
                cardArray.Add(newCard);

            return true;
        }

        static bool AddToCategory(JObject data, string category, long id)
        {
            if (data == null || string.IsNullOrEmpty(category) || category.Trim().ToLower() == "card")
                return false;

            var array = GetCategoryArray(data, category);
            string idStr = id.ToString(CultureInfo.InvariantCulture);

            foreach (var token in array)
            {
                if (token.ToString() == idStr)
                    return false;
            }

            array.Insert(0, id);
            return true;
        }

        static bool MoveIdToFrontInAllCategories(JObject data, long id)
        {
            if (data == null)
                return false;

            bool changed = false;

            foreach (var prop in data.Properties())
            {
                if (string.Equals(prop.Name, "card", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (prop.Value is JArray array)
                    changed |= MoveIdToFront(array, id);
            }

            return changed;
        }

        static bool MoveIdToFront(JArray array, long id)
        {
            if (array == null)
                return false;

            string idStr = id.ToString(CultureInfo.InvariantCulture);

            for (int i = 0; i < array.Count; i++)
            {
                var token = array[i];
                if (token?.ToString() == idStr)
                {
                    if (i == 0)
                        return false;

                    token.Remove();
                    array.Insert(0, token);
                    return true;
                }
            }

            return false;
        }

        static bool RemoveFromCategory(JObject data, string category, long id)
        {
            if (data == null || string.IsNullOrEmpty(category) || category.Trim().ToLower() == "card")
                return false;

            if (data[category] is not JArray array)
                return false;

            return RemoveFromArray(array, id);
        }

        static bool RemoveIdFromAllCategories(JObject data, long id)
        {
            if (data == null)
                return false;

            bool changed = false;

            foreach (var property in data.Properties().ToList())
            {
                if (property.Name == "card")
                    continue;

                if (property.Value is JArray array && RemoveFromArray(array, id))
                    changed = true;
            }

            return changed;
        }

        static bool RemoveCard(JObject data, long id)
        {
            if (data == null)
                return false;

            var cardArray = GetCardArray(data);
            string idStr = id.ToString(CultureInfo.InvariantCulture);

            foreach (var card in cardArray.Children<JObject>().ToList())
            {
                var token = card["id"];
                if (token != null && token.ToString() == idStr)
                {
                    card.Remove();
                    return true;
                }
            }

            return false;
        }

        static JArray GetCardArray(JObject data)
        {
            if (data["card"] is JArray array)
                return array;

            array = new JArray();
            data["card"] = array;
            return array;
        }

        static JArray GetCategoryArray(JObject data, string category)
        {
            if (data[category] is JArray array)
                return array;

            array = new JArray();
            data[category] = array;
            return array;
        }

        static bool RemoveFromArray(JArray array, long id)
        {
            string idStr = id.ToString(CultureInfo.InvariantCulture);

            foreach (var token in array.ToList())
            {
                if (token.ToString() == idStr)
                {
                    token.Remove();
                    return true;
                }
            }

            return false;
        }

        static void Save(SyncUserContext sqlDb, SyncUserBookmarkSqlModel entity, JObject data)
        {
            if (entity == null)
                return;

            entity.data = data.ToString(Formatting.None);
            entity.updated = DateTime.UtcNow;

            if (entity.Id == 0)
                sqlDb.bookmarks.Add(entity);
            else
                sqlDb.bookmarks.Update(entity);

            sqlDb.SaveChanges();
        }

        JsonResult JsonSuccess() => Json(new { success = true });

        JsonResult JsonFailure() => Json(new { success = false });

        async Task<(BookmarkEventPayload payload, string json)> ReadPayloadAsync()
        {
            string body = null;
            var payload = new BookmarkEventPayload();

            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                    return (payload, body);

                try
                {
                    var job = JsonConvert.DeserializeObject<JObject>(body);
                    if (job == null)
                        return (payload, body);

                    payload.Method = job.Value<string>("method");
                    payload.Where = job.Value<string>("where") ?? job.Value<string>("list");
                    payload.CardIdRaw = job.Value<string>("id") ?? job.Value<string>("card_id");

                    if (job.TryGetValue("card", out var cardToken))
                        payload.Card = ConvertToCard(cardToken);
                }
                catch
                {
                }
            }

            return (payload, body);
        }

        static JObject ConvertToCard(JToken token)
        {
            if (token == null)
                return null;

            if (token.Type == JTokenType.Object)
                return (JObject)token;

            if (token.Type == JTokenType.String)
                return ParseCardString(token.Value<string>());

            return null;
        }

        static JObject ParseCardString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<JObject>(value);
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region BookmarkEventPayload
        sealed class BookmarkEventPayload
        {
            public string Method { get; set; }

            public string Where { get; set; }

            public JObject Card { get; set; }

            public string CardIdRaw { get; set; }

            public string NormalizedMethod => string.IsNullOrWhiteSpace(Method) ? null : Method.Trim().ToLowerInvariant();

            public long? ResolveCardId()
            {
                if (!string.IsNullOrWhiteSpace(CardIdRaw) && long.TryParse(CardIdRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;

                var token = Card?["id"];
                if (token != null)
                {
                    if (token.Type == JTokenType.Integer)
                        return token.Value<long>();

                    if (long.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                        return parsed;
                }

                return null;
            }
        }
        #endregion
    }
}
