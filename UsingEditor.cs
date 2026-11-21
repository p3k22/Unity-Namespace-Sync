namespace P3k.FF7R.Editor.Tools
{
   using System.Collections.Generic;
   using System.Text;
   using System.Text.RegularExpressions;

   ///<summary>
   /// Adds and removes plain using lines in C# source text, keeping the file's line breaks and indent.
   ///</summary>
   internal static class UsingEditor
   {
      ///<summary>
      /// Deletes every plain using line that names one of the given namespaces.
      ///</summary>
      internal static string Remove(string text, ICollection<string> namespaces)
      {
         if (namespaces.Count == 0)
         {
            return text;
         }

         SourceMasker.Mask(text, out var masked, out _);
         var builder = new StringBuilder();
         var copiedUpTo = 0;

         foreach (Match match in DeclarationReader.PlainUsing.Matches(masked))
         {
            if (!namespaces.Contains(match.Groups["name"].Value))
            {
               continue;
            }

            var lineEnd = SourceText.StartOfNextLine(text, match.Index + match.Length);
            var removeEnd = SourceText.IsBlank(text, match.Index + match.Length, lineEnd)
               ? lineEnd
               : match.Index + match.Length;

            builder.Append(text, copiedUpTo, match.Index - copiedUpTo);
            copiedUpTo = removeEnd;
         }

         return builder.Append(text, copiedUpTo, text.Length - copiedUpTo).ToString();
      }

      ///<summary>
      /// Adds a using line for each namespace. They go after the file's last using (inside the namespace block when
      /// the usings live there), or above the namespace, or above the first code when the file has neither.
      ///</summary>
      internal static string Insert(string text, ICollection<string> namespaces)
      {
         if (namespaces.Count == 0)
         {
            return text;
         }

         SourceMasker.Mask(text, out var masked, out _);
         var lastUsing = DeclarationReader.FindLastUsing(masked);
         var insertAt = lastUsing == null
            ? FindLineAboveDeclarations(masked)
            : SourceText.SkipClosingDirectives(
               text,
               SourceText.StartOfNextLine(text, lastUsing.Index + lastUsing.Length));

         if (insertAt < 0)
         {
            return text;
         }

         var indent = lastUsing == null ? string.Empty : SourceText.LeadingWhitespace(text, lastUsing.Index);
         var newline = SourceText.DetectNewline(text);
         var block = new StringBuilder();

         block.Append(insertAt == text.Length && !text.EndsWith('\n') ? newline : string.Empty);
         foreach (var namespaceName in namespaces)
         {
            block.Append(indent).Append("using ").Append(namespaceName).Append(';').Append(newline);
         }

         block.Append(lastUsing == null ? newline : string.Empty);
         return text.Insert(insertAt, block.ToString());
      }

      ///<summary>
      /// Start of the namespace line, or of the first code line when there is no namespace; -1 when there is no code.
      ///</summary>
      private static int FindLineAboveDeclarations(string masked)
      {
         var declaration = DeclarationReader.FindNamespaceDeclaration(masked);
         return declaration == null
            ? SourceText.StartOfFirstCodeLine(masked)
            : SourceText.StartOfLine(masked, declaration.Index);
      }
   }
}
