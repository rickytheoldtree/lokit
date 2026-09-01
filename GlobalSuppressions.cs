using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the window lifetime; MainWindow disposes the session from its Closed handler.",
    Scope = "type",
    Target = "~T:LocalizationTranslationWPF.MainWindow")]
