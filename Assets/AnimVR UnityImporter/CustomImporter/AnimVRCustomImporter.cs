using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AnimVRFilePlugin
{
    public class CustomImporter
    {
        public virtual List<PlayableData> Import(string path) { return null; }
        public virtual void Export(StageData stage, string path) { }
    }

    public class CustomImporterAttribute : Attribute
    {
        public string Extension;
        public List<string> Extensions;

        public CustomImporterAttribute(params string[] exts)
        {
            Extensions = new List<string>(exts);
        }
    }
}
