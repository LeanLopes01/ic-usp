using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Carrega a imagem local de um token do grid (MapTokenDraggable.ImagemPath) num RawImage — mesmo
/// padrão de Salas/Chat/ChatAvatarLoader.cs (que faz o mesmo pra avatares da Steam), só que lendo
/// direto de um arquivo local em vez de uma chamada assíncrona da API da Steam — sem inversão de
/// linhas pra corrigir aqui, isso é um problema específico da API da Steam (ver ChatAvatarLoader).
/// Usado por SocialTurnoJogadorItemUI.ConfigurarTurno pra mostrar o mesmo ícone que o token tem no
/// mapa (MapManager), em vez de um avatar Steam — um token de monstro não tem dono Steam nenhum.
/// </summary>
public static class TokenImagemLoader
{
    public static void CarregarImagemEm(RawImage destino, string caminho)
    {
        if (destino == null || string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;

        byte[] bytes = File.ReadAllBytes(caminho);
        var textura = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!textura.LoadImage(bytes))
        {
            Debug.LogWarning($"[TokenImagemLoader] Falha ao decodificar imagem: {caminho}");
            Object.Destroy(textura);
            return;
        }

        destino.texture = textura;
    }
}
