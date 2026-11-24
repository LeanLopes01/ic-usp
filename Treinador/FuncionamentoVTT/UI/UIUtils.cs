using UnityEngine;

public static class UIUtils
{
    /// <summary>
    /// Instancia um prefab e corrige o Z do RectTransform para 0,
    /// evitando o bug de Z negativo acumulado em layouts de UI.
    /// </summary>
    public static GameObject Spawn(GameObject prefab, Transform parent)
    {
        GameObject go = Object.Instantiate(prefab, parent);
        ResetZ(go);
        return go;
    }

    public static GameObject Spawn(GameObject prefab, Transform parent, bool worldPositionStays)
    {
        GameObject go = Object.Instantiate(prefab, parent, worldPositionStays);
        ResetZ(go);
        return go;
    }

    private static void ResetZ(GameObject go)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) return;
        Vector3 lp = rt.localPosition;
        lp.z = 0f;
        rt.localPosition = lp;

        foreach (RectTransform child in rt)
        {
            ResetZ(child.gameObject);
        }
    }
}
