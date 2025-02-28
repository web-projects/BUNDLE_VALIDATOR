﻿using System.Collections.Generic;

namespace Common.XO.Private
{
    public class LinkDALRequestIPA5Object
    {
        public LinkDALActionResponse DALResponseData { get; set; }
        
		public DALCDBData DALCdbData { get; set; }
	 
        public string SignatureName { get; set; }
        public List<byte[]> SignatureData { get; set; }
        public byte[] ESignatureImage { get; set; }
        public int MaxBytes { get; set; }
    }
}
