﻿using Microsoft.AspNetCore.Mvc;

namespace SISI.Controllers.HQporner
{
    public class ViewController : BaseSisiController
    {
        [HttpGet]
        [Route("hqr/vidosik")]
        async public ValueTask<ActionResult> Index(string uri)
        {
            var init = await loadKit(AppInit.conf.HQporner);
            if (await IsBadInitialization(init, rch: true))
                return badInitMsg;

            reset: var rch = new RchClient(HttpContext, host, init, requestInfo);
            var proxyManager = new ProxyManager(init);
            var proxy = proxyManager.Get();

            if (rch.IsNotSupport("web", out string rch_error))
                return OnError(rch_error);

            string memKey = rch.ipkey($"HQporner:view:{uri}", proxyManager);
            if (!hybridCache.TryGetValue(memKey, out Dictionary<string, string> stream_links))
            {
                if (rch.IsNotConnected())
                    return ContentTo(rch.connectionMsg);

                stream_links = await HQpornerTo.StreamLinks(init.corsHost(), uri, 
                               htmlurl => rch.enable ? rch.Get(init.cors(htmlurl), httpHeaders(init)) : Http.Get(init.cors(htmlurl), timeoutSeconds: 8, proxy: proxy, headers: httpHeaders(init)), 
                               iframeurl => rch.enable ? rch.Get(init.cors(iframeurl), httpHeaders(init)) : Http.Get(init.cors(iframeurl), timeoutSeconds: 8, proxy: proxy, headers: httpHeaders(init)));

                if (stream_links == null || stream_links.Count == 0)
                {
                    if (IsRhubFallback(init))
                        goto reset;

                    return OnError("stream_links", proxyManager);
                }

                if (!rch.enable)
                    proxyManager.Success();

                hybridCache.Set(memKey, stream_links, cacheTime(20, init: init));
            }

            return OnResult(stream_links, init, proxy);
        }
    }
}
