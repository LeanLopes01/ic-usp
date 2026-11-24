/// <summary>Modelo de AUTORIA de uma propriedade do personagem (ver Sistemas/SistemasCustom/
/// CustomSystemManager, painel "Motor de Dados") — separado de PropriedadePersonagem (que é só
/// cálculo, sem label) porque "label" é uma preocupação só da Forja, não do motor de efeitos.
/// CarregadorMotorDeDados.CriarEstadoInicial converte isto pra PropriedadePersonagem, descartando
/// o label.</summary>
public class DefinicaoPropriedade
{
    public string id;
    public string label;
    public float valorBase;
    public string formula; // vazio = valor fixo (valorBase); preenchido = derivada (ver PropriedadePersonagem)
}
