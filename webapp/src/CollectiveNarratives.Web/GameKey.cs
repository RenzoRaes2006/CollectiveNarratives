using System.Security.Cryptography;
using System.Text;

namespace CollectiveNarratives.Web;

// Geheime sleutel waarmee alleen de game sessies mag aanmaken. Online staat hij in /opt/collective-narratives/.env,
// lokaal is het "dev-game-key" (appsettings.Development.json).
public sealed class GameKey
{
    readonly byte[] _key;

    public GameKey(IConfiguration config)
    {
        string? key = config["GAME_KEY"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 8)
            throw new InvalidOperationException("GAME_KEY ontbreekt of is te kort (minstens 8 tekens). Zet hem als omgevingsvariabele.");
        _key = Encoding.UTF8.GetBytes(key);
    }

    public bool Matches(string token) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), _key);
}
