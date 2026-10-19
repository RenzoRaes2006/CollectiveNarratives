using CollectiveNarratives.Web;

// Stem-website voor Home Alone VR: telefoons (/poll/{code}), groot scherm (/screen) en de SignalR-hub voor de game.
// Afspraken met Unity: webapp/UNITY-KOPPELING.md
var app = WebApp.Create(new WebApplicationOptions { Args = args });
app.Run();
