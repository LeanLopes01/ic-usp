using System.Collections.Generic;

/// <summary>Resultado puro de uma rolagem do motor de dados (ver MotorDeDados.Rolar) — sem nenhuma
/// formatação/apresentação, isso fica por conta de quem exibe (ver CustomSystemManager, painel de
/// teste). Nome "...Motor" pra não colidir com Salas/Chat/RolagemDados.cs.ResultadoRolagem — este
/// projeto não usa namespaces, então os dois tipos convivem no mesmo escopo global.</summary>
public class ResultadoRolagemMotor
{
    public string idAcao;
    public string idTipoRolagem;
    public List<int> dados = new List<int>();
    public float total;
    public string grau;
}
