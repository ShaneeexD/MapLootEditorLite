using System;

namespace MapLootEditorLite.Client
{
    // Scene Pick Filter matching (case-insensitive), applied per hierarchy path segment (names between '/').
    //
    // A filter WITHOUT '/' matches a segment when the segment:
    //   - equals the filter                      ("LOD"          matches "LOD"),
    //   - or starts with the filter and the next character is not a letter
    //                                            ("LOD0", "LOD_Objects", "LOD (1)", "spawns!" match "LOD"/"SPAWNS").
    // It does NOT match when the filter is only buried inside a longer word or appears later in the name:
    //   "SOO_LOD0", "Wall_LOD1", "LODGroup_Object", "Blod" are not matched by "LOD".
    //
    // A filter WITH '/' is an explicit path fragment and is matched as a case-insensitive substring of the full path.
    internal static class PickFilterMatcher
    {
        public static bool TryMatch(string path, string filter, out string matchedSegment)
        {
            matchedSegment = null;
            if (string.IsNullOrEmpty(path) || string.IsNullOrWhiteSpace(filter))
                return false;

            filter = filter.Trim();
            if (filter.IndexOf('/') >= 0)
            {
                if (path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                matchedSegment = filter;
                return true;
            }

            var start = 0;
            while (start <= path.Length)
            {
                var end = path.IndexOf('/', start);
                if (end < 0) end = path.Length;
                var length = end - start;
                if (length > 0 && SegmentMatches(path, start, length, filter))
                {
                    matchedSegment = path.Substring(start, length);
                    return true;
                }
                start = end + 1;
            }
            return false;
        }

        private static bool SegmentMatches(string path, int start, int length, string filter)
        {
            if (length < filter.Length)
                return false;
            if (string.Compare(path, start, filter, 0, filter.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            return length == filter.Length || !char.IsLetter(path[start + filter.Length]);
        }
    }
}
