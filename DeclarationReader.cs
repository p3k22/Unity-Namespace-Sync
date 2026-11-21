namespace P3k.FF7R.Editor.Tools.NamespaceSync
{
   using System.Collections.Generic;
   using System.Text.RegularExpressions;

   ///<summary>
   /// Reads the namespace, the top level type names, the usings and the identifiers out of masked C# source (the text
   /// that SourceMasker returns, where comments and strings are blank).
   ///</summary>
   internal static class DeclarationReader
   {
      internal static readonly Regex NamespaceDeclaration = new(
         @"\bnamespace\s+(?<name>[\p{L}_@][\w.@]*)\s*(?<end>[{;])");

      internal static readonly Regex UsingDirective = new(
         @"^[ \t]*(global\s+)?using\s+[^;(){}]*;",
         RegexOptions.Multiline);

      internal static readonly Regex PlainUsing = new(
         @"^[ \t]*(global\s+)?using\s+(?<name>[\p{L}_@][\w.@]*)\s*;",
         RegexOptions.Multiline);

      private static readonly Regex TypeDeclaration = new(
         @"\b(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>[\p{L}_]\w*)");

      private static readonly Regex SimpleIdentifier = new(@"(?<![\w.@])[\p{L}_]\w*");

      ///<summary>
      /// The first top level namespace declaration, or null when the file has none.
      ///</summary>
      internal static Match FindNamespaceDeclaration(string masked)
      {
         var declarations = SourceText.KeepAtDepth(NamespaceDeclaration.Matches(masked), masked, 0);
         return declarations.Count > 0 ? declarations[0] : null;
      }

      ///<summary>
      /// The name of the file's top level namespace, or an empty string when it has none.
      ///</summary>
      internal static string FindNamespace(string masked)
      {
         var declaration = FindNamespaceDeclaration(masked);
         return declaration == null ? string.Empty : declaration.Groups["name"].Value;
      }

      ///<summary>
      /// The names of the types declared directly in the namespace (or at the top of a file without one).
      ///</summary>
      internal static List<string> ReadTypeNames(string masked)
      {
         var declaration = FindNamespaceDeclaration(masked);
         var typeDepth = declaration != null && declaration.Groups["end"].Value == "{" ? 1 : 0;
         var names = new List<string>();

         foreach (var match in SourceText.KeepAtDepth(TypeDeclaration.Matches(masked), masked, typeDepth))
         {
            var name = match.Groups["name"].Value;
            if (name != "where")
            {
               names.Add(name);
            }
         }

         return names;
      }

      ///<summary>
      /// The namespaces brought in by plain using lines such as using A.B; (aliases and using static are left out).
      ///</summary>
      internal static HashSet<string> ReadImportedNamespaces(string masked)
      {
         var imported = new HashSet<string>();

         foreach (Match match in PlainUsing.Matches(masked))
         {
            imported.Add(match.Groups["name"].Value);
         }

         return imported;
      }

      ///<summary>
      /// Every name that is used on its own, not after a dot, so A in A.B counts but B does not.
      ///</summary>
      internal static HashSet<string> ReadIdentifiers(string masked)
      {
         var identifiers = new HashSet<string>();

         foreach (Match match in SimpleIdentifier.Matches(masked))
         {
            identifiers.Add(match.Value);
         }

         return identifiers;
      }

      ///<summary>
      /// The last using line a new using can follow: a top level one, or one directly inside the namespace block.
      /// Returns null when the file has no usings.
      ///</summary>
      internal static Match FindLastUsing(string masked)
      {
         var candidates = SourceText.KeepAtDepth(UsingDirective.Matches(masked), masked, 0);
         var declaration = FindNamespaceDeclaration(masked);

         if (declaration != null && declaration.Groups["end"].Value == "{")
         {
            var blockOpen = declaration.Groups["end"].Index;
            var insideBlock = SourceText.KeepAtDepth(UsingDirective.Matches(masked), masked, 1);
            candidates.AddRange(insideBlock.FindAll(match => match.Index > blockOpen));
         }

         Match last = null;
         foreach (var candidate in candidates)
         {
            last = last == null || candidate.Index > last.Index ? candidate : last;
         }

         return last;
      }
   }
}
