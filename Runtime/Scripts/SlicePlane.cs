using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class SlicePlane : MonoBehaviour
{
    public ComputeShader marchingShader;

    ComputeBuffer _segmentsBuffer;
    ComputeBuffer _segmentsCountBuffer;
    ComputeBuffer _weightsBuffer;
    // ComputeBuffer _vectorsBuffer;

    public NoiseGenerator noiseGenerator;

    public Material segmentMat;
    public Mesh segmentMesh;
    
    public GameObject segmentPrefab;
    public Transform planeIntersectParent;
    public GameObject grabPlane;
    public Surface closestSurface;
    
    private List<Surface> _surfaces;
    private int _orientation;
    private Transform _transform;

    int _planeGridPoints;

    private void Awake()
    {
        _planeGridPoints = GridMetrics.PointsPerChunk * 2 + 16;
        CreateBuffers();
        
        _surfaces = new List<Surface>();
        
        _transform = transform;
    }

    public void AttachSurface(Surface surface)
    {
        // we don't want multiple references
        if (_surfaces.Contains(surface)) return;
        
        this._surfaces.Add(surface);
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    struct Segment
    {
        public Vector3 a;
        public Vector3 b;

        public static int SizeOf => sizeof(float) * 3 * 2;
        public new string ToString => $"{a}, {b}";
    }

    float[] _weights;

    public void Render()
    {
        noiseGenerator.isPlane = true;
        noiseGenerator.planeForward = _transform.forward ;
        noiseGenerator.planeRight = _transform.right ;
        _weights = noiseGenerator.GetNoisePlane(_planeGridPoints);
        
        foreach (var t in _surfaces)
        {
            Debug.Log("found surface " + t);
            if ((t.transform.position - transform.position).magnitude < 1) //should this be 1?
            {
                closestSurface = t;
            }
        }
        if (closestSurface) {
            noiseGenerator.offset = _transform.position - closestSurface.transform.position;
            noiseGenerator.size = closestSurface.size;
            noiseGenerator.function = closestSurface.function;
            noiseGenerator.orientation = closestSurface.orientation;
        }
        else
        {
            Debug.LogWarning("no closest surface detected");
        }
        
        ConstructLine();
    }

    Vector3 _oldPosition = Vector3.zero;
    Quaternion _oldRotation = Quaternion.identity;

    void Update()
    {
        if ((_oldPosition - transform.position).magnitude > 0 || (_oldRotation.eulerAngles - transform.rotation.eulerAngles).magnitude > 0)
        {
            Render();
            _oldPosition = _transform.position;
            _oldRotation = _transform.rotation;
        }

        switch (_orientation)
        {
            case 0:
                transform.SetPositionAndRotation(grabPlane.transform.position, grabPlane.transform.rotation);
                break;

            case 1:
                transform.position = new Vector3(0.0f, grabPlane.transform.position.y, 0.0f);
                break;

            case 2:
                transform.position = new Vector3(grabPlane.transform.position.x, 1.5f, 0.0f);
                break;

            case 3:
                transform.position = new Vector3(0.0f, 1.5f, grabPlane.transform.position.z);
                break;
        }

                grabPlane.transform.localPosition = new Vector3(0.0f, 0.0f, 0.0f);
    }

    void ConstructLine()
    {
        marchingShader.SetBuffer(0, "_Segments", _segmentsBuffer);
        marchingShader.SetBuffer(0, "_Weights", _weightsBuffer);
        marchingShader.SetFloat("_OtherSize", 1.0f);
        marchingShader.SetInt("_ChunkSize", _planeGridPoints);
        marchingShader.SetFloat("_IsoLevel", .5f);
        marchingShader.SetVector("_PlaneForward", transform.forward);
        marchingShader.SetVector("_PlaneRight", transform.right);
        // MarchingShader.SetBuffer(0, "_Vectors", _vectorsBuffer);

        _weightsBuffer.SetData(_weights);
        _segmentsBuffer.SetCounterValue(0);

        marchingShader.Dispatch(0, _planeGridPoints / GridMetrics.NumThreads, _planeGridPoints / GridMetrics.NumThreads, 1);

        // segmentMat.SetBuffer("_Vectors", _vectorsBuffer);
        // var bounds = new Bounds(transform.position, new Vector3(GridMetrics.PointsPerChunk, GridMetrics.PointsPerChunk, GridMetrics.PointsPerChunk));
        // Graphics.DrawMeshInstancedProcedural(segmentMesh, 0, segmentMat, bounds, GridMetrics.PointsPerChunk * GridMetrics.PointsPerChunk);

        Segment[] segments = new Segment[ReadSegmentCount()];
        _segmentsBuffer.GetData(segments);

        
        foreach (Transform child in planeIntersectParent)
        {
            GameObject.Destroy(child.gameObject);
        }
        

        foreach (Segment seg in segments)
        {
            var position = _transform.position;
            var rightVec = _transform.right;
            var forwardVec = _transform.forward;
            Vector3 pt1 = position + (rightVec * seg.a.x + forwardVec * seg.a.z) * (1.0f / 100);
            Vector3 pt2 = position + (rightVec * seg.b.x + forwardVec * seg.b.z) * (1.0f / 100);
            Vector3 pos = (pt1 + pt2) * 0.5f;
            Vector3 fromVolume = (pos - closestSurface.transform.position) * 100.0f;


            if (Mathf.Abs(fromVolume.x) < GridMetrics.PointsPerChunk &&
                Mathf.Abs(fromVolume.y) < GridMetrics.PointsPerChunk &&
                Mathf.Abs(fromVolume.z) < GridMetrics.PointsPerChunk)
            {
                Vector3 dir = (pt2 - pt1);
                if (!segmentPrefab)
                {
                    Debug.LogWarning("no segment prefab");
                }
                else
                {
                    GameObject newSeg = Instantiate(segmentPrefab, pos, Quaternion.FromToRotation(Vector3.up, dir),
                        planeIntersectParent);
                    newSeg.transform.localScale = new Vector3(0.005f, dir.magnitude * 0.6f, 0.005f);
                }
            }
        }

    }

    private void OnDrawGizmos()
    {
        if (_weights == null || _weights.Length == 0)
        {
            return;
        }
        for (int x = 0; x < _planeGridPoints; x++)
        {
            for (int y = 0; y < _planeGridPoints; y++)
            {
                int index = x + _planeGridPoints * y;
                float noiseValue = _weights[index];
                const double tolerance = Double.Epsilon;
                if (Math.Abs(noiseValue - (-1)) < tolerance)
                {
                    Gizmos.color = Color.blue;
                }
                else
                {
                    Gizmos.color = noiseValue > 0.5f ? Color.black : Color.white;
                }
                Gizmos.DrawCube(_transform.position + (_transform.right * (x - _planeGridPoints / 2) + _transform.forward * (y - _planeGridPoints / 2)) * (1.0f / 100), Vector3.one * .004f);
            }
        }
    }

    int ReadSegmentCount()
    {
        int[] segCount = { 0 };
        ComputeBuffer.CopyCount(_segmentsBuffer, _segmentsCountBuffer, 0);
        _segmentsCountBuffer.GetData(segCount);
        return segCount[0];
    }

    void CreateBuffers()
    {
        _segmentsBuffer = new ComputeBuffer(5 * (_planeGridPoints * _planeGridPoints), Segment.SizeOf, ComputeBufferType.Append);
        _segmentsCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        _weightsBuffer = new ComputeBuffer(_planeGridPoints * _planeGridPoints * _planeGridPoints, sizeof(float));
        // _vectorsBuffer = new ComputeBuffer(planeGridPoints * planeGridPoints, 3 * 4 * sizeof(float));
    }

    void ReleaseBuffers()
    {
        _segmentsBuffer.Release();
        _segmentsCountBuffer.Release();
        _weightsBuffer.Release();
        // _vectorsBuffer.Release();
    }

    public void ChangeVisibility()
    {
        Renderer ren = GetComponent<Renderer>();
        bool visibility = ren.enabled;
        ren.enabled = !visibility;
        int numberOfChildren = transform.childCount;
        for (int i=0; i<numberOfChildren; i++)
        {
            if (transform.GetChild(i).gameObject.CompareTag("grabPlane"))
            {
                continue;
            }
            ren = transform.GetChild(i).gameObject.GetComponent<Renderer>();
            ren.enabled = !visibility;
        }

    }

    public void SetOrientation(int or)
    {
        _orientation = or;
    }

    public void Release()
    {
        grabPlane.GetComponent<XRGrabInteractable>().trackRotation = true;
    }

}
