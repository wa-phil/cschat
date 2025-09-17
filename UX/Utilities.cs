using System;
using System.Text;
using System.Linq;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

public static class Utilities
{
    /// <summary>
    /// Truncates a plain (non-HTML) string to a maximum length.
    /// Optionally preserves whole words and adds an ellipsis.
    /// Handles null/whitespace gracefully.
    /// </summary>
    public static string TruncatePlain(string? input, int maxLength, bool preserveWords = true, bool addEllipsis = true)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        if (maxLength <= 0) return string.Empty;
        if (input.Length <= maxLength) return input;

        // Reserve room for ellipsis if requested
        var softMax = addEllipsis && maxLength > 1 ? Math.Max(1, maxLength - 1) : maxLength;
        var slice = input.AsSpan(0, softMax);

        if (preserveWords)
        {
            // Try to backtrack to a reasonable boundary (space or punctuation)
            int back = LastBoundaryIndex(slice);
            if (back > 0)
                slice = slice[..back];
        }

        var result = slice.ToString().TrimEnd();
        if (addEllipsis && result.Length < input.Length)
            result += "…"; // single-character ellipsis (fits UI better)
        return result;
    }

    /// <summary>
    /// A stricter truncation that never returns more than maxLength characters,
    /// even including the ellipsis. (Useful for tight column layouts.)
    /// </summary>
    public static string TruncatePlainHard(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input) || maxLength <= 0) return string.Empty;
        if (input.Length <= maxLength) return input;
        if (maxLength == 1) return "…";
        return input.Substring(0, maxLength - 1) + "…";
    }

    private static int LastBoundaryIndex(ReadOnlySpan<char> s)
    {
        for (int i = s.Length - 1; i >= 0; i--)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || IsPunctuation(c))
            {
                // avoid returning index of trailing punctuation itself; cut AFTER it
                return i > 0 ? i : 0;
            }
        }
        return 0;
    }

    private static bool IsPunctuation(char c)
        => c == '.' || c == ',' || c == ';' || c == ':' || c == '!' || c == '?' || c == ')' || c == ']' || c == '}';

    internal static string StripHtml(string input, params Func<string, string>[] customRules)
    {
        var ret = customRules.Aggregate(input, (current, rule) => rule(current));
        ret = Regex.Replace(ret, "<.*?>", " ");
        ret = ret.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
        ret = ret.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);
        ret = ret.Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase);
        ret = ret.Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase);
        ret = ret.Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);
        while (ret.Contains("  ", StringComparison.Ordinal) || ret.Contains("\t", StringComparison.Ordinal)) { ret = Regex.Replace(ret, "  |\t", " "); }
        ret = String.Join(Environment.NewLine, ret.Split(Environment.NewLine).Select(x => x.Trim()).Where(x => x.Length > 0));

        var oldLines = ret.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()!;
        var newLines = new List<string>();
        for (var i = 0; i < oldLines.Count; i++)
        {
            var line = oldLines[i];

            // If the line starts with "<!--" then consume until the line that contains "-->"
            if (line.Equals("<!--", StringComparison.Ordinal))
            {
                // Consume until the line that contains "-->"
                while (i < oldLines.Count && !oldLines[i].Contains("-->", StringComparison.Ordinal)) { i++; }

                // Consume the line that contains "-->"
                continue;
            }
            // Remove the line if it ends with "{behavior:url(#default#VML);}"
            else if (line.EndsWith("{behavior:url(#default#VML);}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            newLines.Add(line);
        }
        ret = string.Join(Environment.NewLine, newLines);

        return ret;
    }

    internal static string StripRTF(string inputRtf) => RichTextStripper.StripRichTextFormat(inputRtf);

    // Renderer helpers are now on the Table class. Provide compatibility wrappers.
    public static Table ToTable(IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        var hs = headers?.ToList() ?? new List<string>();
        var rs = rows?.ToList() ?? new List<string[]>();
        return new Table(hs, rs);
    }

    public static string ToTable(IEnumerable<string> headers, IEnumerable<string[]> rows, int maxWidth = 140)
    {
        var t = ToTable(headers, rows);
        return t.ToText(maxWidth);
    }

    public static string ToCsv(IReadOnlyList<string> headers, List<string[]> rows)
        => ToTable(headers, rows).ToCsv();

    public static string ToJson(IReadOnlyList<string> headers, List<string[]> rows)
        => ToTable(headers, rows).ToJson();

    #region "RTF Stripping"

    /// <summary>
    /// Rich Text Stripper
    /// </summary>
    /// <remarks>
    /// Translated from Python located at:
    /// http://stackoverflow.com/a/188877/448
    /// </remarks>
    internal static class RichTextStripper
    {
        private class StackEntry
        {
            public int NumberOfCharactersToSkip { get; set; }
            public bool Ignorable { get; set; }

            public StackEntry(int numberOfCharactersToSkip, bool ignorable)
            {
                NumberOfCharactersToSkip = numberOfCharactersToSkip;
                Ignorable = ignorable;
            }
        }

        private static readonly Regex _rtfRegex = new Regex(@"\\([a-z]{1,32})(-?\d{1,10})?[ ]?|\\'([0-9a-f]{2})|\\([^a-z])|([{}])|[\r\n]+|(.)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly List<string> destinations = new List<string>
        {
            "aftncn","aftnsep","aftnsepc","annotation","atnauthor","atndate","atnicn","atnid",
            "atnparent","atnref","atntime","atrfend","atrfstart","author","background",
            "bkmkend","bkmkstart","blipuid","buptim","category","colorschememapping",
            "colortbl","comment","company","creatim","datafield","datastore","defchp","defpap",
            "do","doccomm","docvar","dptxbxtext","ebcend","ebcstart","factoidname","falt",
            "fchars","ffdeftext","ffentrymcr","ffexitmcr","ffformat","ffhelptext","ffl",
            "ffname","ffstattext","field","file","filetbl","fldinst","fldrslt","fldtype",
            "fname","fontemb","fontfile","fonttbl","footer","footerf","footerl","footerr",
            "footnote","formfield","ftncn","ftnsep","ftnsepc","g","generator","gridtbl",
            "header","headerf","headerl","headerr","hl","hlfr","hlinkbase","hlloc","hlsrc",
            "hsv","htmltag","info","keycode","keywords","latentstyles","lchars","levelnumbers",
            "leveltext","lfolevel","linkval","list","listlevel","listname","listoverride",
            "listoverridetable","listpicture","liststylename","listtable","listtext",
            "lsdlockedexcept","macc","maccPr","mailmerge","maln","malnScr","manager","margPr",
            "mbar","mbarPr","mbaseJc","mbegChr","mborderBox","mborderBoxPr","mbox","mboxPr",
            "mchr","mcount","mctrlPr","md","mdeg","mdegHide","mden","mdiff","mdPr","me",
            "mendChr","meqArr","meqArrPr","mf","mfName","mfPr","mfunc","mfuncPr","mgroupChr",
            "mgroupChrPr","mgrow","mhideBot","mhideLeft","mhideRight","mhideTop","mhtmltag",
            "mlim","mlimloc","mlimlow","mlimlowPr","mlimupp","mlimuppPr","mm","mmaddfieldname",
            "mmath","mmathPict","mmathPr","mmaxdist","mmc","mmcJc","mmconnectstr",
            "mmconnectstrdata","mmcPr","mmcs","mmdatasource","mmheadersource","mmmailsubject",
            "mmodso","mmodsofilter","mmodsofldmpdata","mmodsomappedname","mmodsoname",
            "mmodsorecipdata","mmodsosort","mmodsosrc","mmodsotable","mmodsoudl",
            "mmodsoudldata","mmodsouniquetag","mmPr","mmquery","mmr","mnary","mnaryPr",
            "mnoBreak","mnum","mobjDist","moMath","moMathPara","moMathParaPr","mopEmu",
            "mphant","mphantPr","mplcHide","mpos","mr","mrad","mradPr","mrPr","msepChr",
            "mshow","mshp","msPre","msPrePr","msSub","msSubPr","msSubSup","msSubSupPr","msSup",
            "msSupPr","mstrikeBLTR","mstrikeH","mstrikeTLBR","mstrikeV","msub","msubHide",
            "msup","msupHide","mtransp","mtype","mvertJc","mvfmf","mvfml","mvtof","mvtol",
            "mzeroAsc","mzeroDesc","mzeroWid","nesttableprops","nextfile","nonesttables",
            "objalias","objclass","objdata","object","objname","objsect","objtime","oldcprops",
            "oldpprops","oldsprops","oldtprops","oleclsid","operator","panose","password",
            "passwordhash","pgp","pgptbl","picprop","pict","pn","pnseclvl","pntext","pntxta",
            "pntxtb","printim","private","propname","protend","protstart","protusertbl","pxe",
            "result","revtbl","revtim","rsidtbl","rxe","shp","shpgrp","shpinst",
            "shppict","shprslt","shptxt","sn","sp","staticval","stylesheet","subject","sv",
            "svb","tc","template","themedata","title","txe","ud","upr","userprops",
            "wgrffmtfilter","windowcaption","writereservation","writereservhash","xe","xform",
            "xmlattrname","xmlattrvalue","xmlclose","xmlname","xmlnstbl",
            "xmlopen"
        };

        private static readonly Dictionary<string, string> specialCharacters = new Dictionary<string, string>
        {
            { "par", "\n" },
            { "sect", "\n\n" },
            { "page", "\n\n" },
            { "line", "\n" },
            { "tab", "\t" },
            { "emdash", "\u2014" },
            { "endash", "\u2013" },
            { "emspace", "\u2003" },
            { "enspace", "\u2002" },
            { "qmspace", "\u2005" },
            { "bullet", "\u2022" },
            { "lquote", "\u2018" },
            { "rquote", "\u2019" },
            { "ldblquote", "\u201C" },
            { "rdblquote", "\u201D" },
        };

        public static string StripRichTextFormat(string inputRtf)
        {
            string returnString;

            var stack = new Stack<StackEntry>();
            bool ignorable = false;              // Whether this group (and all inside it) are "ignorable".
            int ucskip = 1;                      // Number of ASCII characters to skip after a unicode character.
            int curskip = 0;                     // Number of ASCII characters left to skip
            var outList = new List<string>();    // Output buffer.

            MatchCollection matches = _rtfRegex.Matches(inputRtf);

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    string word = match.Groups[1].Value;
                    string arg = match.Groups[2].Value;
                    string hex = match.Groups[3].Value;
                    string character = match.Groups[4].Value;
                    string brace = match.Groups[5].Value;
                    string tchar = match.Groups[6].Value;

                    if (!String.IsNullOrEmpty(brace))
                    {
                        curskip = 0;
                        if (brace == "{")
                        {
                            // Push state
                            stack.Push(new StackEntry(ucskip, ignorable));
                        }
                        else if (brace == "}")
                        {
                            // Pop state
                            StackEntry entry = stack.Pop();
                            ucskip = entry.NumberOfCharactersToSkip;
                            ignorable = entry.Ignorable;
                        }
                    }
                    else if (!String.IsNullOrEmpty(character)) // \x (not a letter)
                    {
                        curskip = 0;
                        if (character == "~")
                        {
                            if (!ignorable)
                            {
                                outList.Add("\xA0");
                            }
                        }
                        else if ("{}\\".Contains(character))
                        {
                            if (!ignorable)
                            {
                                outList.Add(character);
                            }
                        }
                        else if (character == "*")
                        {
                            ignorable = true;
                        }
                    }
                    else if (!String.IsNullOrEmpty(word)) // \foo
                    {
                        curskip = 0;
                        if (destinations.Contains(word))
                        {
                            ignorable = true;
                        }
                        else if (ignorable)
                        {
                        }
                        else if (specialCharacters.ContainsKey(word))
                        {
                            outList.Add(specialCharacters[word]);
                        }
                        else if (word == "uc")
                        {
                            ucskip = Int32.Parse(arg);
                        }
                        else if (word == "u")
                        {
                            int c = Int32.Parse(arg);
                            if (c < 0)
                            {
                                c += 0x10000;
                            }
                            //Ein gültiger UTF32-Wert ist zwischen 0x000000 und 0x10ffff (einschließlich) und sollte keine Ersatzcodepunktwerte (0x00d800 ~ 0x00dfff)
                            if (c >= 0x000000 && c <= 0x10ffff && (c < 0x00d800 || c > 0x00dfff))
                                outList.Add(Char.ConvertFromUtf32(c));
                            else outList.Add("?");
                            curskip = ucskip;
                        }
                    }
                    else if (!String.IsNullOrEmpty(hex)) // \'xx
                    {
                        if (curskip > 0)
                        {
                            curskip -= 1;
                        }
                        else if (!ignorable)
                        {
                            int c = Int32.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                            outList.Add(Char.ConvertFromUtf32(c));
                        }
                    }
                    else if (!String.IsNullOrEmpty(tchar))
                    {
                        if (curskip > 0)
                        {
                            curskip -= 1;
                        }
                        else if (!ignorable)
                        {
                            outList.Add(tchar);
                        }
                    }
                }
            }
            else
            {
                // Didn't match the regex
                returnString = inputRtf;
            }

            returnString = String.Join(String.Empty, outList.ToArray());

            return returnString;
        }
    }
    #endregion
}