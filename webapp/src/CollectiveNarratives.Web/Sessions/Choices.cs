namespace CollectiveNarratives.Web.Sessions;

// Ingebouwde keuzes (vroeger choices.js). Stuurt de game bij StartVote geen vraag en opties mee,
// dan haalt de server ze hier op aan de hand van het id.
public static class Choices
{
    public sealed record Choice(string Question, IReadOnlyList<OptionDef> Options);

    public static readonly IReadOnlyDictionary<string, Choice> All = new Dictionary<string, Choice>
    {
        ["telefoon"] = new("The phone is ringing. What does Kevin do?", [new("opnemen", "Answer it"), new("rinkelen", "Let it ring")]),
        ["hond"] = new("Scratching at the back door. It's Marley's dog.", [new("binnen", "Let the dog in"), new("buiten", "Leave it outside")]),
        ["stroom"] = new("The power goes out. What does Kevin do?", [new("kelder", "Go to the basement"), new("zaklamp", "Find a flashlight")]),
        ["inbreker"] = new("The back door opens. What does Kevin do?", [new("kast", "Hide in the closet"), new("raam", "Escape through the window")]),
    };

    public static readonly IReadOnlyList<string> Endings = ["held", "weg", "cadeau"];
}
