﻿using Shared.Models.Online.Tortuga;

namespace Shared.Models.Online.Eneyida
{
    public class EmbedModel
    {
        public bool IsEmpty { get; set; }

        public string content { get; set; }

        public string quel { get; set; }

        public Voice[] serial { get; set; }

        public List<Similar> similars { get; set; }
    }
}
