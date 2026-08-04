using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DynamicTriangle : MonoBehaviour
{
    Mesh mesh;
    Vector3[] vertices;

    void Start()
    {
        mesh = new Mesh();
        mesh.MarkDynamic();

        vertices = new Vector3[]
        {
            new Vector3(-1,0,0),
            new Vector3( 1,0,0),
            new Vector3( 0,1,0)
        };

        mesh.vertices = vertices;
        mesh.triangles = new int[]{0,1,2};
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    void Update()
    {
        vertices[2].y = 1.0f + Mathf.Sin(Time.time);

        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
    }
}