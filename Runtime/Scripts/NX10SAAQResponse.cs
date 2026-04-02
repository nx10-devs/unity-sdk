using System;
using System.Collections.Generic;

namespace NX10
{
    [Serializable]
    public class SAAQResponse
    {
        public string status;
        public SAAQData data;

        public bool HasPrompt => data != null;
    }

    [Serializable]
    public class SAAQData
    {
        public string triggerID;
        public bool dismissable;
        public SAAQBlock prompt;
    }

    [Serializable]
    public class SAAQBlock
    {
        public string id;
        public string blockType;
        public string questionText;

        // Logic for saaqType1 (Slider)
        public string leftAnchorValue;
        public string rightAnchorValue;
        public int? rangeSize;
        public int? startingValue;
        public bool? confirmButtonEnabled;

        // Logic for saaqType2 (Multiple Choice / Categorical)
        public bool? multipleSelect;
        public List<SAAQOption> options;
    }

    [Serializable]
    public class SAAQOption
    {
        public string id;
        public Feeling feeling;
        public List<SAAQBlock> followonQuestion;
    }

    public class Feeling
    {
        public string id;
        public string feelingsType;
        public string displayName;
        public string suggestedEmoji;
    }
}
