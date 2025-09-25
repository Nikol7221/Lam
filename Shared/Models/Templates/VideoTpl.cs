﻿using Shared.Models.Base;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models.Templates
{
    public static class VideoTpl
    {
        public static string ToJson(string method, string url, string title, in StreamQualityTpl? streamquality = null, in SubtitleTpl? subtitles = null, string quality = null, VastConf vast = null, List<HeadersModel> headers = null, int? hls_manifest_timeout = null, SegmentTpl segments = null)
        {
            return JsonSerializer.Serialize(new
            {
                title,
                method,
                url,
                headers = headers != null ? headers.ToDictionary(k => k.name, v => v.val) : null,
                quality = streamquality?.ToObject() ?? new StreamQualityTpl(new List<(string, string)>() { (url, quality??"auto") }).ToObject(),
                subtitles = subtitles?.ToObject(),
                hls_manifest_timeout,
                vast = vast ?? AppInit.conf.vast,
                segments = segments?.ToObject()

            }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault });
        }
    }
}
