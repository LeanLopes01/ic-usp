using System.Collections.Generic;

/// <summary>Configuração de UM jeito de resolver uma rolagem (ex: "linear_check" pro d20 padrão de
/// D&D, "dice_pool" pro pool de d10 de Vampire) — ver MotorDeDados.Rolar/RegistrarResolvedor.
/// limiarSucesso/parMaximo só têm efeito em "dice_pool" (o resolvedor de "linear_check" ignora os
/// dois).</summary>
public class DefinicaoTipoRolagem
{
    public string id;
    public int dado;          // faces do dado (20 pro d20 de linear_check, 10 pro d10 de dice_pool)
    public int limiarSucesso; // dice_pool: valor mínimo do dado pra contar como sucesso (ex: 8)
    public int parMaximo;     // dice_pool: valor do dado que conta pra "par de máximos" (ex: 10)
    public List<DefinicaoGrauResultado> graus = new List<DefinicaoGrauResultado>();
}
