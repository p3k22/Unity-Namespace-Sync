namespace P3k.FF7R.Editor.Tools
{
   using System;
   using System.Text;
   using System.Text.RegularExpressions;

   ///<summary>
   /// Gives C# source text the namespace it should have: renames the one it has, or wraps the code in a new block
   /// after the usings. Works on text only, so it can be tested without Unity.
   ///</summary>
   internal static class NamespaceRewriter
   {
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
         var declarations = SourceText.KeepAtDepth(
            DeclarationReader.NamespaceDeclaration.Matches(masked),
            masked,
            0);

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
         if (bodyStart < 0 || SourceText.IsBlank(masked, bodyStart, masked.Length))
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
         var usings = SourceText.KeepAtDepth(DeclarationReader.UsingDirective.Matches(masked), masked, 0);
         hasUsings = usings.Count > 0;

         if (!hasUsings)
         {
            return SourceText.StartOfFirstCodeLine(masked);
         }

         var lastUsing = usings[usings.Count - 1];
         var lineAfterUsing = SourceText.StartOfNextLine(text, lastUsing.Index + lastUsing.Length);
         return SourceText.SkipClosingDirectives(text, lineAfterUsing);
      }

      ///<summary>
      /// Finds where the block must end. Trailing #endif or #endregion lines that close one opened before the block
      /// are left outside it, so the #if still wraps the namespace.
      ///</summary>
      private static int FindBodyEnd(string text, int bodyStart)
      {
         var unmatchedClosers = 0;

         var lineStart = bodyStart;
         while (lineStart < text.Length)
         {
            unmatchedClosers += SourceText.IsClosingDirective(text, lineStart) ? 1 : 0;
            unmatchedClosers -= SourceText.IsOpeningDirective(text, lineStart) ? 1 : 0;
            lineStart = SourceText.StartOfNextLine(text, lineStart);
         }

         var bodyEnd = text.Length;
         while (unmatchedClosers-- > 0)
         {
            var lastLine = SourceText.StartOfLastNonBlankLine(text, bodyEnd);
            if (lastLine < 0 || !SourceText.IsClosingDirective(text, lastLine))
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
            var lineEnd = Math.Min(SourceText.StartOfNextLine(text, lineStart), end);

            if (!SourceText.IsBlank(text, lineStart, lineEnd) && !insideString[lineStart])
            {
               builder.Append(indent);
            }

            builder.Append(text, lineStart, lineEnd - lineStart);
            lineStart = lineEnd;
         }

         return builder.ToString();
      }
   }
}
