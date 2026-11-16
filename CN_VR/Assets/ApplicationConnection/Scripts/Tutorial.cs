using UnityEngine;

// Plek voor de tutorial. ApplicationConnection roept Begin() aan zodra het grote scherm gekoppeld is
// en wacht tot IsDone true is; dan start de game (StartGame). Het publiek joint ondertussen.
// Deze basisversie is meteen klaar (test-scene ApplicationConnection). De echte tutorial: StreetTutorial.
public class Tutorial : MonoBehaviour
{
    public bool IsDone { get; protected set; }

    // Bord waarop ApplicationConnection de code en verbindingstekst zet. Null = het paneel dat je blik volgt.
    public virtual TutorialSign Sign => null;

    public virtual void Begin()
    {
        IsDone = true;
    }

    // Sessie verloren: tutorial terug naar het begin (nieuwe code komt eraan).
    public virtual void ResetTutorial()
    {
        IsDone = false;
    }
}
