using UnityEngine;

// Plek voor de tutorial. ApplicationConnection roept Begin() aan zodra het grote scherm gekoppeld is
// en wacht tot IsDone true is (en minstens 60 s voorbij zijn, zodat het publiek kan joinen).
// Nu nog leeg: meteen klaar. Later: echte tutorial (bv. leren grijpen) en pas op het einde IsDone = true.
public class Tutorial : MonoBehaviour
{
    public bool IsDone { get; protected set; }

    public virtual void Begin()
    {
        IsDone = true;
    }
}
