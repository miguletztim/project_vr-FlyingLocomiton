using UnityEngine;

public class PlayspaceBoundary : MonoBehaviour
{
    public Material gridMaterial;
    
    [Header("Boundary Settings")]
    public float paddingDistance = 1.0f; // Abstand zur echten Guardian Boundary
    
    private GameObject boundaryGrid;
    private LineRenderer boundaryLineRenderer;
    private float gridWidth;
    private float gridDepth;
    
    void Start()
    {
        CreateGridWithPadding();
        AnchorToTrackingSpace();
    }
    
    void AnchorToTrackingSpace()
    {
        // Grid am TrackingSpace verankern - dort ist der Ursprung der realen Welt
        OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();
        
        if (cameraRig != null && cameraRig.trackingSpace != null)
        {
            boundaryGrid.transform.SetParent(cameraRig.trackingSpace);
            boundaryGrid.transform.localPosition = new Vector3(0, 0.01f, 0);
            boundaryGrid.transform.localRotation = Quaternion.identity;
            
            Debug.Log("Grid am TrackingSpace verankert - folgt der echten Welt!");
        }
        else
        {
            Debug.LogWarning("OVRCameraRig nicht gefunden. Grid bleibt in Weltkoordinaten.");
            boundaryGrid.transform.position = new Vector3(0, 0.01f, 0);
        }
    }
    
    void CreateGridWithPadding()
    {
        // Versuche Guardian-Dimensionen zu bekommen
        if (OVRManager.boundary.GetConfigured())
        {
            Vector3 guardianDimensions = OVRManager.boundary.GetDimensions(OVRBoundary.BoundaryType.PlayArea);
            
            // Ziehe Padding von allen Seiten ab
            gridWidth = Mathf.Max(0.5f, guardianDimensions.x - (paddingDistance * 2));
            gridDepth = Mathf.Max(0.5f, guardianDimensions.z - (paddingDistance * 2));
            
            Debug.Log($"Guardian-Größe: {guardianDimensions.x}m x {guardianDimensions.z}m");
            Debug.Log($"Grid mit Padding: {gridWidth}m x {gridDepth}m");
        }
        else
        {
            Debug.LogWarning("Guardian nicht konfiguriert. Nutze Standard-Größe mit Padding.");
            gridWidth = 2f;
            gridDepth = 2f;
        }
        
        CreateGrid(gridWidth, gridDepth);
        CreateBoundaryLines(gridWidth, gridDepth);
    }
    
    void CreateGrid(float width, float depth)
    {
        boundaryGrid = new GameObject("BoundaryGrid");
        
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.parent = boundaryGrid.transform;
        floor.transform.localPosition = new Vector3(0, 0, 0);
        floor.transform.localScale = new Vector3(width / 10f, 1, depth / 10f);
        
        Renderer renderer = floor.GetComponent<Renderer>();
        
        if (gridMaterial != null)
        {
            renderer.material = gridMaterial;
            renderer.material.SetColor("_GridColor", new Color(0, 1, 0, 0.5f));
        }
        else
        {
            Debug.LogError("Grid Material ist nicht zugewiesen!");
        }
        
        Destroy(floor.GetComponent<Collider>());
    }
    
    void CreateBoundaryLines(float width, float depth)
    {
        GameObject boundaryLine = new GameObject("BoundaryLine");
        boundaryLine.transform.parent = boundaryGrid.transform;
        
        boundaryLineRenderer = boundaryLine.AddComponent<LineRenderer>();
        boundaryLineRenderer.positionCount = 5;
        boundaryLineRenderer.startWidth = 0.02f;
        boundaryLineRenderer.endWidth = 0.02f;
        boundaryLineRenderer.useWorldSpace = false; // Lokale Koordinaten
        
        Material lineMaterial = new Material(Shader.Find("Sprites/Default"));
        boundaryLineRenderer.material = lineMaterial;
        boundaryLineRenderer.startColor = new Color(1, 1, 0, 0.9f);
        boundaryLineRenderer.endColor = new Color(1, 1, 0, 0.9f);
        
        UpdateBoundaryLinePositions(width, depth);
    }
    
    void UpdateBoundaryLinePositions(float width, float depth)
    {
        if (boundaryLineRenderer == null) return;
        
        float halfWidth = width / 2f;
        float halfDepth = depth / 2f;
        
        // Lokale Positionen relativ zum Grid
        boundaryLineRenderer.SetPosition(0, new Vector3(-halfWidth, 0.1f, -halfDepth));
        boundaryLineRenderer.SetPosition(1, new Vector3(halfWidth, 0.1f, -halfDepth));
        boundaryLineRenderer.SetPosition(2, new Vector3(halfWidth, 0.1f, halfDepth));
        boundaryLineRenderer.SetPosition(3, new Vector3(-halfWidth, 0.1f, halfDepth));
        boundaryLineRenderer.SetPosition(4, new Vector3(-halfWidth, 0.1f, -halfDepth));
    }
    
    void OnEnable()
    {
        OVRManager.BoundaryVisibilityChanged += OnBoundaryVisibilityChanged;
    }
    
    void OnDisable()
    {
        OVRManager.BoundaryVisibilityChanged -= OnBoundaryVisibilityChanged;
    }
    
    private void OnBoundaryVisibilityChanged(OVRPlugin.BoundaryVisibility visibility)
    {
        if (boundaryGrid == null) return;
        
        Renderer renderer = boundaryGrid.GetComponentInChildren<Renderer>();
        if (renderer == null) return;
        
        if (visibility == OVRPlugin.BoundaryVisibility.NotSuppressed)
        {
            Debug.Log("⚠️ Guardian ist sichtbar - Spieler nähert sich der ECHTEN Grenze!");
            renderer.material.SetColor("_GridColor", new Color(1, 0, 0, 0.9f));
            OVRInput.SetControllerVibration(0.8f, 0.8f, OVRInput.Controller.Touch);
        }
        else
        {
            Debug.Log("✓ Guardian nicht sichtbar - Im sicheren Bereich");
            renderer.material.SetColor("_GridColor", new Color(0, 1, 0, 0.5f));
            OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.Touch);
        }
    }
    
    void Update()
    {
        CheckDistanceToInnerBoundary();
    }
    
    void CheckDistanceToInnerBoundary()
    {
        if (boundaryGrid == null || Camera.main == null) return;
        
        // Spielerposition in Weltkoordinaten
        Vector3 headPosition = Camera.main.transform.position;
        
        // Grid-Center in Weltkoordinaten
        Vector3 gridCenter = boundaryGrid.transform.position;
        
        // Relative Position zum Grid-Center (in der echten Welt)
        float relativeX = headPosition.x - gridCenter.x;
        float relativeZ = headPosition.z - gridCenter.z;
        
        float halfWidth = gridWidth / 2f;
        float halfDepth = gridDepth / 2f;
        
        // Distanz zur inneren Grenze
        float distanceToInnerEdge = Mathf.Min(
            halfWidth - Mathf.Abs(relativeX),
            halfDepth - Mathf.Abs(relativeZ)
        );
        
        UpdateGridColor(distanceToInnerEdge);
    }
    
    void UpdateGridColor(float distanceToInnerBoundary)
    {
        if (boundaryGrid == null) return;
        
        Renderer renderer = boundaryGrid.GetComponentInChildren<Renderer>();
        if (renderer == null) return;
        
        Color newColor;
        
        if (distanceToInnerBoundary < 0)
        {
            // Spieler hat innere Grenze überschritten
            newColor = new Color(1, 0, 0, 0.8f);
            
            if (Time.frameCount % 30 == 0)
            {
                OVRInput.SetControllerVibration(0.3f, 0.3f, OVRInput.Controller.Touch);
            }
        }
        else if (distanceToInnerBoundary < 0.3f)
        {
            newColor = new Color(1, 0.5f, 0, 0.7f); // Orange
        }
        else if (distanceToInnerBoundary < 0.6f)
        {
            newColor = new Color(1, 1, 0, 0.6f); // Gelb
        }
        else
        {
            newColor = new Color(0, 1, 0, 0.5f); // Grün
        }
        
        renderer.material.SetColor("_GridColor", newColor);
    }
}