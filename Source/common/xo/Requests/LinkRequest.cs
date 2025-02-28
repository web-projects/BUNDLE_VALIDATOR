﻿using System.Collections.Generic;
using Common.XO.Private;

namespace Common.XO.Requests
{
    public partial class LinkRequest
    {
        public string MessageID { get; set; }
        public int Timeout { get; set; }
        public List<LinkActionRequest> Actions { get; set; }
        public LinkRequestIPA5Object LinkObjects { get; set; }
    }
}
