using System.Collections.Generic;
using UnityEngine;

// Gera os centros de célula cobertos por uma forma de área (ver SelecaoAlvoManager) — usado tanto
// pra prévia de área (segue o mouse ou fica ancorada no token, ver AcaoStatBlock.origemNoToken)
// quanto pro anel de alcance (sempre Circulo, ver SelecaoAlvoManager.IniciarSelecao). Aproximações
// de grid quadrado — mesmo espírito "melhor esforço" do resto do sistema de mira, não regras 100%
// fiéis de mesa (ex: contagem de diagonal alternada da 5e).
public static class FormaAreaGeometria
{
    public static List<Vector3> GerarCelulas(FormaAreaAcao forma, Vector3 origemMundo, Vector3 direcaoAlvoMundo,
        int raioQuadrados, float tamanhoCelula)
    {
        switch (forma)
        {
            case FormaAreaAcao.Circulo: return GerarCirculo(origemMundo, raioQuadrados, tamanhoCelula);
            case FormaAreaAcao.Cone: return GerarConeOuLinha(origemMundo, direcaoAlvoMundo, raioQuadrados, tamanhoCelula, ehCone: true);
            case FormaAreaAcao.Linha: return GerarConeOuLinha(origemMundo, direcaoAlvoMundo, raioQuadrados, tamanhoCelula, ehCone: false);
            default: return GerarQuadrado(origemMundo, raioQuadrados, tamanhoCelula);
        }
    }

    private static List<Vector3> GerarQuadrado(Vector3 centroSnapado, int raioQuadrados, float tamanhoCelula)
    {
        var celulas = new List<Vector3>();
        for (int dy = -raioQuadrados; dy <= raioQuadrados; dy++)
            for (int dx = -raioQuadrados; dx <= raioQuadrados; dx++)
                celulas.Add(centroSnapado + new Vector3(dx * tamanhoCelula, dy * tamanhoCelula, 0f));
        return celulas;
    }

    // Distância euclidiana centro-a-centro <= raio, com uma pequena margem pra não cortar células
    // quase-tangentes na borda (aproximação comum de VTT, evita um "círculo" visivelmente quadrado
    // demais nas pontas).
    private static List<Vector3> GerarCirculo(Vector3 centroSnapado, int raioQuadrados, float tamanhoCelula)
    {
        var celulas = new List<Vector3>();
        float raioMundo = raioQuadrados * tamanhoCelula + tamanhoCelula * 0.3f;
        for (int dy = -raioQuadrados; dy <= raioQuadrados; dy++)
        {
            for (int dx = -raioQuadrados; dx <= raioQuadrados; dx++)
            {
                Vector3 offset = new Vector3(dx * tamanhoCelula, dy * tamanhoCelula, 0f);
                if (offset.magnitude <= raioMundo)
                    celulas.Add(centroSnapado + offset);
            }
        }
        return celulas;
    }

    // Cone: ápice em origemMundo, direção = rumo a direcaoAlvoMundo, comprimento = raio. Célula entra
    // se a projeção ao longo da direção ("forward") está entre 0 e o comprimento E a distância
    // lateral (perpendicular) é <= forward/2 — regra 5e de "largura no fim = comprimento".
    // Linha: mesma projeção, mas largura fixa de 1 quadrado (regra 5e — todas as linhas têm 5 pés de
    // largura, não escala com o comprimento).
    private static List<Vector3> GerarConeOuLinha(Vector3 origemMundo, Vector3 direcaoAlvoMundo, int raioQuadrados,
        float tamanhoCelula, bool ehCone)
    {
        var celulas = new List<Vector3>();

        Vector3 direcao = direcaoAlvoMundo - origemMundo;
        direcao.z = 0f;
        if (direcao.sqrMagnitude < 0.0001f) direcao = Vector3.up;
        direcao.Normalize();
        Vector3 lateralUnitario = new Vector3(-direcao.y, direcao.x, 0f);

        float comprimento = raioQuadrados * tamanhoCelula;
        float metadeLarguraLinha = tamanhoCelula * 0.5f;
        // Piso de meia-largura perto do ápice — sem isso a fórmula do cone (forward/2) dá largura
        // zero em forward=0 e a própria célula de origem ficaria de fora.
        float pisoMeiaLarguraCone = tamanhoCelula * 0.25f;

        for (int dy = -raioQuadrados; dy <= raioQuadrados; dy++)
        {
            for (int dx = -raioQuadrados; dx <= raioQuadrados; dx++)
            {
                Vector3 offset = new Vector3(dx * tamanhoCelula, dy * tamanhoCelula, 0f);
                float forward = Vector3.Dot(offset, direcao);
                if (forward < 0f || forward > comprimento) continue;

                float lateral = Mathf.Abs(Vector3.Dot(offset, lateralUnitario));
                float larguraPermitida = ehCone ? Mathf.Max(forward * 0.5f, pisoMeiaLarguraCone) : metadeLarguraLinha;

                if (lateral <= larguraPermitida)
                    celulas.Add(origemMundo + offset);
            }
        }
        return celulas;
    }
}
