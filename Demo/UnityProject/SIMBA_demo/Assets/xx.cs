using UnityEngine;
using M2M.SIMBA;

public class MeshInspector : MonoBehaviour
{
    public ShellMeshLoader loader;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log(loader.RuntimeMesh.GetInstanceID());
            Debug.Log(loader.GetComponent<MeshFilter>().sharedMesh.GetInstanceID());

            Debug.Log(object.ReferenceEquals(
                loader.RuntimeMesh,
                loader.GetComponent<MeshFilter>().sharedMesh));
        }
    }
}