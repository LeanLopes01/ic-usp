using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillUIItem : MonoBehaviour
{
    public Toggle treinadoToggle;
    public TMP_Text nomeText;
    public TMP_Dropdown atributoDropdown; // Opções: 0=FOR, 1=DES, 2=CON, 3=INT, 4=SAB, 5=CAR
    public TMP_Text profBonusText; // O bônus base de proficiência (ex: +2)
    public TMP_InputField miscBonusInput; // Bônus extra que o jogador digita
    public TMP_Text totalText; // A soma de tudo
    public Button invisibleButton; // Para clicar e ver a descrição
    
    [HideInInspector] public string descricaoDb;
    [HideInInspector] public string nomeOriginal;
}