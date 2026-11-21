namespace P3k.FF7R.Editor.Tools
{
   using System;
   using System.Collections.Generic;
   using System.Text;
   using System.Text.RegularExpressions;

   ///<summary>
   /// Gives C# source text the namespace it should have: renames the one it has, or wraps the code in a new block
   /// after the usings. Works on text only, so it can be tested without Unity.
   ///</summary>
   internal static class NamespaceRewriter
   {
      private static readonly Regex NamespaceDeclaration = new(@"\bnamespace\s+(?<name>[\p{L}_@][\w.@]*)\s*[{;]");

      private static readonly Regex UsingDirective = new(
         @"^[ \t]*(global\s+)?using\s+[^;(){}]*;",
         RegexOptions.Multiline);

      private static readonly Regex AssemblyAttribute = new(
         @"^[ \t]*\[\s*(assembly|module)\s*:",
         RegexOptions.Multiline);

      private static readonly Regex FirstIndentedLine = new(@"^( +)\S", RegexOptions.Multiline);

      private static readonly Regex LeadingBlankLines = new(@"\A([ \t]*\r?\n)+");

      private static readonly Regex EndsWithBlankLine = new(@"\n[ \t]*\r?\n\z");

      ///<summary>
      /// Renames the namespace of the text, or adds one when it has none. Returns true when the text changed.
      /// note says what happened, or is null when the namespace was already right.
      ///</summary>
      internal static bool TryRewrite(string text, string namespaceName, out string newText, out string note)
      {
         newText = text;
         note = null;

         SourceMasker.Mask(text, out var masked, out var insideString);
         var declarations = KeepTopLevel(NamespaceDeclaration.Matches(masked), masked);

         if (declarations.Count > 1)
         {
            note = "skipped, it has more than one top level namespace";
            return false;
         }

         return declarations.Count == 1
            ? TryRename(text, declarations[0].Groups["name"], namespaceName, out newText, out note)
            : TryAddNamespace(text, masked, insideString, namespaceName, out newText, out note);
      }

      ///<summary>
      /// Swaps the name of the existing namespace for the wanted one and leaves every other character alone.
      ///</summary>
      private static bool TryRename(
         string text,
         Group existingName,
         string namespaceName,
         out string newText,
         out string note)
      {
         newText = text;
         note = null;

         if (existingName.Value == namespaceName)
         {
            return false;
         }

         newText = text.Substring(0, existingName.Index)
                   + namespaceName
                   + text.Substring(existingName.Index + existingName.Length);
         note = "renamed " + existingName.Value + " -> " + namespaceName;
         return true;
      }

      ///<summary>
      /// Wraps everything after the using lines in a new namespace block, indenting it one level. A trailing #endif or
      /// #endregion that closes one opened above the block stays outside it.
      ///</summary>
      private static bool TryAddNamespace(
         string text,
         string masked,
         bool[] insideString,
         string namespaceName,
         out string newText,
         out string note)
      {
         newText = text;
         note = null;

         if (AssemblyAttribute.IsMatch(masked))
         {
            note = "skipped, it has assembly level attributes";
            return false;
         }

         var bodyStart = FindBodyStart(text, masked, out var hasUsings);
         if (bodyStart < 0 || IsBlank(masked, bodyStart, masked.Length))
         {
            note = "skipped, it has no code";
            return false;
         }

         var bodyEnd = FindBodyEnd(text, bodyStart);
         var newline = text.Contains("\r\n") ? "\r\n" : "\n";
         var indent = DetectIndent(text);

         var head = text.Substring(0, bodyStart);
         var body = IndentLines(text, bodyStart, bodyEnd, insideString, indent);
         body = LeadingBlankLines.Replace(body, string.Empty).TrimEnd();

         var builder = new StringBuilder(head);
         builder.Append(hasUsings && !EndsWithBlankLine.IsMatch(head) ? newline : string.Empty);
         builder.Append("namespace ").Append(namespaceName).Append(newline).Append('{').Append(newline);
         builder.Append(body).Append(newline).Append('}').Append(newline);
         builder.Append(text.Substring(bodyEnd));

         newText = builder.ToString();
         note = "added namespace " + namespaceName;
         return true;
      }

      ///<summary>
      /// Picks the indent the file already uses: a tab, or the width of its first space indented line (4 if none).
      ///</summary>
      private static string DetectIndent(string text)
      {
         if (text.Contains("\n\t"))
         {
            return "\t";
         }

         var firstIndent = FirstIndentedLine.Match(text);
         return firstIndent.Success ? firstIndent.Groups[1].Value : "    ";
      }

      ///<summary>
      /// Finds where the new namespace block starts: the line after the last top level using, or the first line of
      /// code when there are no usings. Returns -1 when the file holds no code at all.
      ///</summary>
      private static int FindBodyStart(string text, string masked, out bool hasUsings)
      {
         var usings = KeepTopLevel(UsingDirective.Matches(masked), masked);
         hasUsings = usings.Count > 0;

         if (!hasUsings)
         {
            return StartOfFirstCodeLine(masked);
         }

         var lastUsing = usings[usings.Count - 1];
         return SkipClosingEndifs(text, StartOfNextLine(text, lastUsing.Index + lastUsing.Length));
      }

      ///<summary>
      /// Moves past #endif and #endregion lines that directly follow the usings, so they are not cut in half.
      ///</summary>
      private static int SkipClosingEndifs(string text, int position)
      {
         while (true)
         {
            var nextLine = SkipBlankLines(text, position);
            if (nextLine >= text.Length || !IsClosingDirective(text, nextLine))
            {
               return position;
            }

            position = StartOfNextLine(text, nextLine);
         }
      }

      ///<summary>
      /// Finds where the block must end. Trailing #endif or #endregion lines that close one opened before the block
      /// are left outside it, so the #if still wraps the namespace.
      ///</summary>
      private static int FindBodyEnd(string text, int bodyStart)
      {
         var unmatchedClosers = 0;

         for (var lineStart = bodyStart; lineStart < text.Length; lineStart = StartOfNextLine(text, lineStart))
         {
            unmatchedClosers += IsClosingDirective(text, lineStart) ? 1 : 0;
            unmatchedClosers -= IsOpeningDirective(text, lineStart) ? 1 : 0;
         }

         var bodyEnd = text.Length;
         while (unmatchedClosers-- > 0)
         {
            var lastLine = StartOfLastNonBlankLine(text, bodyEnd);
            if (lastLine < 0 || !IsClosingDirective(text, lastLine))
            {
               break;
            }

            bodyEnd = lastLine;
         }

         return bodyEnd;
      }

      ///<summary>
      /// Copies the lines between start and end with one indent added to each non blank line. Lines that begin inside
      /// a string literal are copied as they are, so multi line strings keep their content.
      ///</summary>
      private static string IndentLines(string text, int start, int end, bool[] insideString, string indent)
      {
         var builder = new StringBuilder();

         for (var lineStart = start; lineStart < end;)
         {
            var lineEnd = Math.Min(StartOfNextLine(text, lineStart), end);

            if (!IsBlank(text, lineStart, lineEnd) && !insideString[lineStart])
            {
               builder.Append(indent);
            }

            builder.Append(text, lineStart, lineEnd - lineStart);
            lineStart = lineEnd;
         }

         return builder.ToString();
      }

      ///<summary>
      /// Keeps only the matches that are not inside any braces.
      ///</summary>
      private static List<Match> KeepTopLevel(MatchCollection matches, string masked)
      {
         var topLevel = new List<Match>();
         var depth = 0;
         var scannedUpTo = 0;

         foreach (Match match in matches)
         {
            for (var position = scannedUpTo; position < match.Index; position++)
            {
               depth += masked[position] == '{' ? 1 : masked[position] == '}' ? -1 : 0;
            }

            scannedUpTo = match.Index;
            if (depth == 0)
            {
               topLevel.Add(match);
            }
         }

         return topLevel;
      }

      ///<summary>
      /// Index of the first character of the line after the one holding index, or the text length on the last line.
      ///</summary>
      private static int StartOfNextLine(string text, int index)
      {
         var newlineIndex = text.IndexOf('\n', index);
         return newlineIndex < 0 ? text.Length : newlineIndex + 1;
      }

      ///<summary>
      /// Start of the line that holds the first non white space character, or -1 when there is none.
      ///</summary>
      private static int StartOfFirstCodeLine(string masked)
      {
         for (var index = 0; index < masked.Length; index++)
         {
            if (!char.IsWhiteSpace(masked[index]))
            {
               return masked.LastIndexOf('\n', index) + 1;
            }
         }

         return -1;
      }

      ///<summary>
      /// Skips whole blank lines from position and returns the start of the first line that has content.
      ///</summary>
      private static int SkipBlankLines(string text, int position)
      {
         while (position < text.Length)
         {
            var nextLine = StartOfNextLine(text, position);
            if (!IsBlank(text, position, nextLine))
            {
               break;
            }

            position = nextLine;
         }

         return position;
      }

      ///<summary>
      /// Start of the last line before end that has content, or -1 when every line is blank.
      ///</summary>
      private static int StartOfLastNonBlankLine(string text, int end)
      {
         var lineEnd = end;

         while (lineEnd > 0)
         {
            var lineStart = lineEnd == 1 ? 0 : text.LastIndexOf('\n', lineEnd - 2) + 1;
            if (!IsBlank(text, lineStart, lineEnd))
            {
               return lineStart;
            }

            lineEnd = lineStart;
         }

         return -1;
      }

      ///<summary>
      /// Tells whether the line starting at lineStart is an #if or #region.
      ///</summary>
      private static bool IsOpeningDirective(string text, int lineStart)
      {
         return LineStartsWith(text, lineStart, "#if") || LineStartsWith(text, lineStart, "#region");
      }

      ///<summary>
      /// Tells whether the line starting at lineStart is an #endif or #endregion.
      ///</summary>
      private static bool IsClosingDirective(string text, int lineStart)
      {
         return LineStartsWith(text, lineStart, "#endif") || LineStartsWith(text, lineStart, "#endregion");
      }

      ///<summary>
      /// Tells whether the line starting at lineStart begins, after any indent, with the given token.
      ///</summary>
      private static bool LineStartsWith(string text, int lineStart, string token)
      {
         var index = lineStart;
         while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
         {
            index++;
         }

         return string.CompareOrdinal(text, index, token, 0, token.Length) == 0;
      }

      ///<summary>
      /// Tells whether every character from start up to end is white space.
      ///</summary>
      private static bool IsBlank(string text, int start, int end)
      {
         for (var index = start; index < end; index++)
         {
            if (!char.IsWhiteSpace(text[index]))
            {
               return false;
            }
         }

         return true;
      }
   }
}
