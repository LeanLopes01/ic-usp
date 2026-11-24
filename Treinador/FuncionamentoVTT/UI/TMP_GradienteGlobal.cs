using UnityEngine;
using TMPro;

[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class TMP_GradienteGlobal : MonoBehaviour
{
    [Header("Configuração do Gradiente")]
    [Tooltip("Cor no início do texto (esquerda).")]
    public Color corInicio = Color.white;
    [Tooltip("Cor no final do texto (direita).")]
    public Color corFim = Color.white;
    
    [Tooltip("Marque se o texto pode mudar durante o jogo (ex: contador de vida, nomes variados).")]
    public bool atualizarSempre = false;

    private TMP_Text textComponent;

    void Awake()
    {
        textComponent = GetComponent<TMP_Text>();
    }

    void Start()
    {
        AplicarGradiente();
    }

    void Update()
    {
        // Se o texto for dinâmico, precisamos recalcular sempre que ele mudar
        if (atualizarSempre && textComponent.havePropertiesChanged)
        {
            AplicarGradiente();
        }
    }

    public void AplicarGradiente()
    {
        // Força o TMP a gerar a malha para podermos ler os vértices
        textComponent.ForceMeshUpdate();
        
        TMP_TextInfo textInfo = textComponent.textInfo;
        int characterCount = textInfo.characterCount;

        if (characterCount == 0) return;

        // Pega as medidas totais da caixa de texto
        Bounds bounds = textComponent.bounds;
        float minX = bounds.min.x;
        float maxX = bounds.max.x;
        float larguraTotal = maxX - minX;

        // Prevenção de divisão por zero caso o texto seja minúsculo ou bugado
        if (larguraTotal <= 0) return;

        // Passa por cada caractere e pinta os 4 vértices dele
        for (int i = 0; i < characterCount; i++)
        {
            if (!textInfo.characterInfo[i].isVisible) continue;

            int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
            int vertexIndex = textInfo.characterInfo[i].vertexIndex;
            
            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;
            Color32[] colors = textInfo.meshInfo[materialIndex].colors32;

            for (int j = 0; j < 4; j++)
            {
                // Calcula qual é a porcentagem da posição deste vértice em relação ao todo (de 0.0 a 1.0)
                float t = (vertices[vertexIndex + j].x - minX) / larguraTotal;
                
                // Aplica a cor do gradiente baseada nessa porcentagem
                colors[vertexIndex + j] = Color.Lerp(ResolverCor(corInicio), ResolverCor(corFim), t);
            }
        }

        // Envia as novas cores de volta para o componente renderizar
        textComponent.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    // Color não inicializado (ex: campo criado depois que o componente já existia na cena, antes
    // de ser preenchido no Inspector) serializa como (0,0,0,0) — preto totalmente transparente.
    // Sem essa salvaguarda, o texto inteiro fica invisível em vez de aparecer em cor sólida.
    private static Color ResolverCor(Color cor) => cor.a > 0f ? cor : Color.white;
}