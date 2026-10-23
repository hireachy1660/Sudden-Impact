using UnityEngine;

/// <summary>
/// 단일 서브메쉬 오브젝트에 아웃라인 효과를 추가하는 컴포넌트.
/// Awake 시 동일 메쉬를 참조하는 차일드 렌더러를 자동 생성.
/// WeaponPickupNode의 각 무기 비주얼 오브젝트에 부착.
/// </summary>
public class OutlineEffect : MonoBehaviour
{
    [SerializeField] private Material outlineMaterial;

    private void Awake()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || outlineMaterial == null) return;

        GameObject outlineObj = new GameObject("_Outline");
        outlineObj.transform.SetParent(transform);
        outlineObj.transform.localPosition = Vector3.zero;
        outlineObj.transform.localRotation = Quaternion.identity;
        outlineObj.transform.localScale = Vector3.one;

        outlineObj.AddComponent<MeshFilter>().mesh = mf.sharedMesh;
        outlineObj.AddComponent<MeshRenderer>().material = outlineMaterial;
    }
}
