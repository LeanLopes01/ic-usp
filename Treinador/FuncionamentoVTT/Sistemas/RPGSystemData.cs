using System;
using UnityEngine;

/// <summary>
/// Dados de um sistema de RPG detectado ou criado localmente.
/// </summary>
[Serializable]
public class RPGSystemData
{
    public string systemName;
    public string folderName;
    public string description;       // breve — categoria do wizard (etapa1)
    public string descricaoDetalhe;  // completo — tópicos de regras_jogando.json formatados
    public Sprite icon;
    public bool isEditable;
}
