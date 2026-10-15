using MacroDeck.Localization;

namespace SystemHotkeys;

// For text handed to something outside Macro Deck, which takes plain strings.
internal static class PlainText
{
	private static readonly LocalizationResolver _resolver = CreateResolver();

	internal static string Resolve(LocalizedText text) => _resolver.Resolve(text, null) ?? string.Empty;

	private static LocalizationResolver CreateResolver()
	{
		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(Strings.LocalizationCatalog);
		return new LocalizationResolver(catalogs);
	}
}
