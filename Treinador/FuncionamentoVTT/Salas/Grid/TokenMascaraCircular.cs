using UnityEngine;

// Gera (uma vez, em cache) um sprite circular branco com borda suavizada via alpha. Usado tanto
// para SpriteMask (token no grid, SpriteRenderer) quanto para UnityEngine.UI.Mask (miniatura do
// token na lista) — assim o token fica redondo mesmo quando a imagem de origem é um quadrado 1:1
// totalmente preenchido, sem depender de a arte já vir com bordas transparentes.
public static class TokenMascaraCircular
{
    private static Sprite _cache;

    public static Sprite Obter()
    {
        if (_cache != null) return _cache;

        const int tamanho = 128;
        var tex = new Texture2D(tamanho, tamanho, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        float raio = tamanho / 2f;
        var centro = new Vector2(raio, raio);
        var pixels = new Color32[tamanho * tamanho];
        for (int y = 0; y < tamanho; y++)
        {
            for (int x = 0; x < tamanho; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centro);
                float alpha = Mathf.Clamp01(raio - dist); // ~1px de anti-aliasing na borda
                pixels[y * tamanho + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        _cache = Sprite.Create(tex, new Rect(0, 0, tamanho, tamanho), new Vector2(0.5f, 0.5f), tamanho);
        return _cache;
    }
}
