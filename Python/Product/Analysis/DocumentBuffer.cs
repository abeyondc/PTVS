﻿// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PythonTools.Parsing;

namespace Microsoft.PythonTools.Analysis {
    class DocumentBuffer {
        public int Version { get; private set; } = -1;
        public StringBuilder Text { get; } = new StringBuilder();

        public void Reset(int version, string content) {
            Version = version;
            Text.Clear();
            Text.Append(content);
        }

        public void Update(IEnumerable<DocumentChangeSet> changes) {
            foreach (var change in changes) {
                Update(change);
            }
        }

        public void Update(DocumentChangeSet changes) {
            if (Version >= 0) {
                if (changes.FromVersion < Version) {
                    return;
                } else if (changes.FromVersion > Version) {
                    throw new InvalidOperationException("missing prior versions");
                }
            }

            int lastStart = -1;
            int delta = 0;
            var lineLoc = SplitLines(Text.ToString()).ToArray();

            foreach (var change in changes.Changes) {
                int start = NewLineLocation.LocationToIndex(lineLoc, change.ReplacedSpan.Start, Text.Length);
                if (start < lastStart) {
                    throw new InvalidOperationException("changes must be in order of start location");
                }
                lastStart = start;

                int end = NewLineLocation.LocationToIndex(lineLoc, change.ReplacedSpan.End, Text.Length);
                if (end > start) {
                    Text.Remove(start + delta, end - start);
                }
                if (!string.IsNullOrEmpty(change.InsertedText)) {
                    Text.Insert(start + delta, change.InsertedText);
                    delta += change.InsertedText.Length;
                }

                delta -= (end - start);
            }

            Version = changes.ToVersion;
        }

        private static IEnumerable<NewLineLocation> SplitLines(string text) {
            NewLineLocation nextLine;

            int lastLineEnd = 0;
            while ((nextLine = NewLineLocation.FindNewLine(text, lastLineEnd)).EndIndex != lastLineEnd) {
                yield return nextLine;
                lastLineEnd = nextLine.EndIndex;
            }

            if (lastLineEnd != text.Length) {
                yield return nextLine;
            }
        }
    }

}
