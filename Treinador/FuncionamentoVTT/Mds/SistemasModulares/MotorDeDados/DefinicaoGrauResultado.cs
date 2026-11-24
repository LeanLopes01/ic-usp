/// <summary>Um grau de resultado nomeado (ex: "critico", "sucesso", "falha") de um
/// DefinicaoTipoRolagem — avaliado em ORDEM DE AUTORIA contra o EstadoPersonagem (já com as
/// pseudo-propriedades da rolagem injetadas, ver MotorDeDados) via
/// AvaliadorDeExpressao.AvaliarCondicao; o primeiro grau cuja condição bater vence (ver
/// MotorDeDados.ClassificarGrau).</summary>
public class DefinicaoGrauResultado
{
    public string id;
    public string condicao;
}
