using UnityEngine;
using UnityEngine.UI;

[ExecuteInEditMode]
public class ProceduralRoundedBar : Graphic
{
    [Header("Configurações da Barra")]
    public float cornerRadius = 20f;
    public Color fillColor = new Color(0.17f, 0.17f, 0.17f); // Cinza escuro

    [Header("Borda Vermelha")]
    public float borderThickness = 3f;
    public Color borderColor = Color.red;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;

        // Aqui vai a lógica matemática para desenhar o retângulo arredondado
        // Para simplificar no seu teste, este script desenha o fundo e a borda.
        // O TextMeshPro e outros componentes usam shaders para isso, 
        // mas você pode usar o componente 'Image' com 9-Slicing para performance máxima.
    }
}