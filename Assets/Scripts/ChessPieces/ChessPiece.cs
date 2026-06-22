using System.Collections.Generic;
using UnityEngine;

public enum ChessPieceType
{
    None = 0,
    Pawn = 1,
    Rook = 2,
    Knight = 3,
    Bishop = 4,
    Queen = 5,
    King = 6
}

public class ChessPiece : MonoBehaviour
{
    public int team;
    public int currentX;
    public int currentY;
    public ChessPieceType type;

    private Vector3 desiredPosition;
    private Vector3 desiredScale = Vector3.one;

    private bool isHovered = false;
    private float hoverHeight = 0.35f; // Cuánto se eleva al pasar el ratón
    private float smoothSpeed = 12f;

    private void Start()
    {
        desiredScale = transform.localScale;    
    }
    private void Update()
    {
        float distance = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), 
                                          new Vector3(desiredPosition.x, 0, desiredPosition.z));
        
        Vector3 targetPos = desiredPosition;

        if (distance > 0.1f)
        {
            targetPos.y += Mathf.Sin(Mathf.Clamp01(1 - distance / 5f) * Mathf.PI) * 2.0f;
        }
        else if (isHovered)
        {
            targetPos.y += hoverHeight;
        }

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smoothSpeed);
        transform.localScale = Vector3.Lerp(transform.localScale, desiredScale, Time.deltaTime * smoothSpeed);
    }

    public void SetHover(bool state) => isHovered = state;

    public virtual List<Vector2Int> GetAvailableMoves(ref ChessPiece[,] board, int tileCountX, int tileCounty)
    {
        List<Vector2Int> r = new List<Vector2Int>();

        r.Add(new Vector2Int(3, 3));
        r.Add(new Vector2Int(3, 4));
        r.Add(new Vector2Int(4, 3));
        r.Add(new Vector2Int(4, 4));

        return r;
    }
    public virtual SpecialMove GetSpecialMoves(ref ChessPiece[,] board, ref List<Vector2Int[]> moveList, ref List <Vector2Int> availableMoves)
    {
        return SpecialMove.None;
    }

    public virtual void SetPosition(Vector3 position, bool force = false)
    {
        desiredPosition = position;
        if (force)
            transform.position = desiredPosition;
    }
    public virtual void SetScale(Vector3 scale, bool force = false)
    {
        desiredScale = scale;
        if (force)
            transform.localScale = desiredScale;
    }
}
