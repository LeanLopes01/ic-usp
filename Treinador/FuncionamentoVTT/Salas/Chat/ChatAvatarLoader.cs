using System;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Busca o avatar real da Steam (não o token do personagem) e aplica num RawImage — extraído de
/// Salas/SteamAmigoItemUI.cs.CarregarAvatar() pra ser reaproveitado pelos 5 prefabs de mensagem de
/// chat (Salas/Chat/ChatMensagem*UI.cs), em vez de duplicar o fetch async + correção de linhas em
/// cada um.
/// </summary>
public static class ChatAvatarLoader
{
    public static async void CarregarAvatarEm(RawImage destino, string steamIdString)
    {
        if (destino == null || string.IsNullOrWhiteSpace(steamIdString)) return;
        if (!ulong.TryParse(steamIdString, out ulong steamIdBruto)) return;

        var steamId = new SteamId { Value = steamIdBruto };

        try
        {
            var img = await SteamFriends.GetLargeAvatarAsync(steamId);
            if (!img.HasValue || destino == null) return;

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
                Array.Copy(dadosOriginais, origemOffset, dadosCorrigidos, destinoOffset, stride);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(dadosCorrigidos);
            tex.Apply();
            destino.texture = tex;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChatAvatarLoader] Avatar de '{steamIdString}' falhou: {e.Message}");
        }
    }
}
