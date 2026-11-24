using System;
using Steamworks;
using Steamworks.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item de UI que representa um amigo Steam na lista de convites.
/// Usado como prefab instanciado pelo SteamConviteUI.
///
/// === INSPECTOR SETUP ===
/// nomeText      -> TMP_Text com o nome do amigo
/// statusText    -> TMP_Text com "Na sala" / "Jogando" / "Online" / "Offline"
/// botaoConvidar -> Button que dispara o convite
/// avatarImage   -> RawImage para o avatar (opcional)
/// </summary>
public class SteamAmigoItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button botaoConvidar;
    [SerializeField] private RawImage avatarImage;

    private Friend amigoAtual;
    private Action<Friend> onConvidarCallback;

    // ==========================================
    // CONFIGURAÇÃO
    // ==========================================
    public void Configurar(Friend amigo, Lobby? salaAtual, Action<Friend> onConvidar)
    {
        amigoAtual = amigo;
        onConvidarCallback = onConvidar;

        if (nomeText != null)
            nomeText.text = amigo.Name;

        bool naLobby = EstaNaLobby(amigo, salaAtual);
        AtualizarStatus(amigo, naLobby);

        if (botaoConvidar != null)
        {
            botaoConvidar.onClick.RemoveAllListeners();
            botaoConvidar.onClick.AddListener(OnClickConvidar);
        }

        if (avatarImage != null)
            CarregarAvatar();
    }

    // ==========================================
    // INTERNOS
    // ==========================================
    private bool EstaNaLobby(Friend amigo, Lobby? salaAtual)
    {
        if (!salaAtual.HasValue) return false;

        foreach (var membro in salaAtual.Value.Members)
        {
            if (membro.Id == amigo.Id)
                return true;
        }
        return false;
    }

    private void AtualizarStatus(Friend amigo, bool naLobby)
    {
        if (botaoConvidar != null)
            botaoConvidar.interactable = !naLobby && amigo.IsOnline;

        if (statusText == null) return;

        if (naLobby)
            statusText.text = "Na sala";
        else if (amigo.IsPlayingThisGame)
            statusText.text = "Jogando este jogo";
        else if (amigo.IsOnline)
            statusText.text = "Online";
        else
            statusText.text = "Offline";
    }

    private void OnClickConvidar()
    {
        onConvidarCallback?.Invoke(amigoAtual);
    }

    private async void CarregarAvatar()
    {
        try
        {
            var img = await SteamFriends.GetLargeAvatarAsync(amigoAtual.Id);
            if (img.HasValue && avatarImage != null)
            {
                int w = (int)img.Value.Width;
                int h = (int)img.Value.Height;

                // A API Steam retorna os pixels de baixo para cima (bottom-to-top).
                // Invertemos as linhas antes de carregar para corrigir a imagem.
                byte[] dadosOriginais = img.Value.Data;
                byte[] dadosCorrigidos = new byte[dadosOriginais.Length];
                int stride = w * 4; // 4 bytes por pixel (RGBA32)
                for (int linha = 0; linha < h; linha++)
                {
                    int origemOffset = linha * stride;
                    int destinoOffset = (h - 1 - linha) * stride;
                    System.Array.Copy(dadosOriginais, origemOffset, dadosCorrigidos, destinoOffset, stride);
                }

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(dadosCorrigidos);
                tex.Apply();
                avatarImage.texture = tex;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamConvite] Avatar de '{amigoAtual.Name}' falhou: {e.Message}");
        }
    }
}
