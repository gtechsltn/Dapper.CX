﻿using System.Collections.Generic;

namespace SampleApp.RazorPages.Interfaces
{
    /// <summary>
    /// allows a page to define one or more code samples from GitHub to import into a Razor page for tutorial purposes
    /// </summary>
    public interface ICodeSample
    {
        IEnumerable<CodeSample> Samples { get; }
    }

    public class CodeSample
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Comments { get; set; }
    }
}