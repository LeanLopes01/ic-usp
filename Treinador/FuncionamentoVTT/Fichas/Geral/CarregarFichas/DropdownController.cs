using UnityEngine;
using TMPro;

public class DropdownController : MonoBehaviour
{
    public TMP_Dropdown dropdown;
    public GameObject opcaoA; // Quinta Edição (D&D SRD)
    public GameObject opcaoB; // Cyberpunk
    public GameObject opcaoC; // Desbravador 2e SRD (Pathfinder)

    void Start()
    {
        dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
        dropdown.value = 0;
        AtivarOpcao(0);
    }

    void OnDropdownValueChanged(int index)
    {
        AtivarOpcao(index);
    }

    void AtivarOpcao(int index)
    {
        if (opcaoA != null) opcaoA.SetActive(false);
        if (opcaoB != null) opcaoB.SetActive(false);
        if (opcaoC != null) opcaoC.SetActive(false);

        switch (index)
        {
            case 0: if (opcaoA != null) opcaoA.SetActive(true); break; // Quinta Edição (D&D)
            case 1: if (opcaoB != null) opcaoB.SetActive(true); break; // Cyberpunk
            case 2: if (opcaoC != null) opcaoC.SetActive(true); break; // Pathfinder
        }
    }
}
