namespace CollectiveNarratives.Web.Sessions;

// Ingebouwde keuzes (vroeger choices.js). Stuurt de game bij StartVote geen vraag en opties mee,
// dan haalt de server ze hier op aan de hand van het id.
public static class Choices
{
    public sealed record Choice(string Question, IReadOnlyList<OptionDef> Options);

    public static readonly IReadOnlyDictionary<string, Choice> All = new Dictionary<string, Choice>
    {
        ["telefoon"] = new("De telefoon gaat. Wat doet Kevin?", [new("opnemen", "Opnemen"), new("rinkelen", "Laten rinkelen")]),
        ["hond"] = new("Gekrab aan de achterdeur. Het is de hond van Marley.", [new("binnen", "Hond binnenlaten"), new("buiten", "Buiten laten")]),
        ["stroom"] = new("De stroom valt uit. Wat doet Kevin?", [new("kelder", "Naar de kelder"), new("zaklamp", "Zaklamp zoeken")]),
        ["inbreker"] = new("De achterdeur gaat open. Wat doet Kevin?", [new("kast", "Verstoppen in de kast"), new("raam", "Vluchten door het raam")]),
    };

    public static readonly IReadOnlyList<string> Endings = ["held", "weg", "cadeau"];
}
