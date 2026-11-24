using System.Collections.Generic;
using UnityEngine;

// Pool de quads coloridos pra destacar células de grid (alcance/área de mira, ver
// SelecaoAlvoManager) — nunca destrói, só desativa sobras e reaproveita no próximo Mostrar (mesmo
// espírito de outros pools do projeto, evita Instantiate/Destroy todo frame). Sprite sólido criado
// em runtime (mesmo padrão de MapManager.CriarSpritePlaceholder), tingido por SpriteRenderer.color
// — assim os dois pools (alcance/área) reaproveitam o MESMO sprite branco compartilhado, só a cor
// da instância muda.
public class GridCellHighlightPool
{
    private static Sprite _spriteQuadradoCompartilhado;

    private readonly Transform _pai;
    private readonly Color _cor;
    private readonly int _sortingOrder;
    private readonly List<SpriteRenderer> _quads = new List<SpriteRenderer>();

    public GridCellHighlightPool(Transform pai, Color cor, int sortingOrder)
    {
        _pai = pai;
        _cor = cor;
        _sortingOrder = sortingOrder;
    }

    public void Mostrar(List<Vector3> centrosDeCelula, float tamanhoCelula)
    {
        if (centrosDeCelula == null) { Esconder(); return; }

        while (_quads.Count < centrosDeCelula.Count)
            _quads.Add(CriarQuad());

        for (int i = 0; i < _quads.Count; i++)
        {
            if (i < centrosDeCelula.Count)
            {
                SpriteRenderer sr = _quads[i];
                sr.transform.position = centrosDeCelula[i];
                sr.transform.localScale = new Vector3(tamanhoCelula, tamanhoCelula, 1f);
                sr.gameObject.SetActive(true);
            }
            else
            {
                _quads[i].gameObject.SetActive(false);
            }
        }
    }

    public void Esconder()
    {
        foreach (SpriteRenderer sr in _quads)
            sr.gameObject.SetActive(false);
    }

    private SpriteRenderer CriarQuad()
    {
        var go = new GameObject("HighlightCelula");
        go.transform.SetParent(_pai, false);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = ObterSpriteQuadradoCompartilhado();
        sr.color = _cor;
        sr.sortingOrder = _sortingOrder;
        return sr;
    }

    private static Sprite ObterSpriteQuadradoCompartilhado()
    {
        if (_spriteQuadradoCompartilhado != null) return _spriteQuadradoCompartilhado;

        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color[4 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();

        _spriteQuadradoCompartilhado = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return _spriteQuadradoCompartilhado;
    }
}
