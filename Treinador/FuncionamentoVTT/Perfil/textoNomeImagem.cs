using UnityEngine;
using TMPro; 
using UnityEngine.UI;

public class UIPlayerInfo : MonoBehaviour
{
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI playerIdText;
    public GameObject avatarGameObject;
    public Image avatarImage;

    void Start()
    {
        if (SteamManager.Instance != null)
        {
            SteamManager.Instance.BindProfileUI(playerNameText, playerIdText, avatarGameObject, avatarImage);

            if (playerNameText != null)
                playerNameText.text = SteamManager.Instance.CurrentSteamName;

            if (playerIdText != null)
                playerIdText.text = SteamManager.Instance.CurrentSteamId;

            return;
        }

        if (DataManager.Instance != null)
        {
            // Pega o nome centralizado no DataManager (seja Login ou Debug)
            playerNameText.text = DataManager.Instance.GetPlayerName();
            if (playerIdText != null)
                playerIdText.text = DataManager.Instance.GetCurrentId().ToString();
        }
        else
        {
            Debug.LogError("UIPlayerInfo: DataManager não encontrado na cena!");
            playerNameText.text = "Erro: Sem DataManager";
            if (playerIdText != null)
                playerIdText.text = "0";
        }
    }
}