using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class King : ChessPiece
{
    public override List<Vector2Int> GetAvailableMoves(ref ChessPiece[,] board, int tileCountX, int tileCountY)
    {
        List<Vector2Int> r = new();

        int[] dx = { 1, 1, 1, 0, 0, -1, -1, -1 };
        int[] dy = { 1, 0, -1, 1, -1, 1, 0, -1 };

        for (int i = 0; i < 8; i++)
        {
            int newX = currentX + dx[i];
            int newY = currentY + dy[i];

            if (newX >= 0 && newX < tileCountX && newY >= 0 && newY < tileCountY)
            {
                if (board[newX, newY] == null || board[newX, newY].team != team)
                    r.Add(new Vector2Int(newX, newY));
            }
        }

        return r;
    }

    public override SpecialMove GetSpecialMoves(ref ChessPiece[,] board, ref List<Vector2Int[]> moveList, ref List<Vector2Int> availableMoves)
    {
        SpecialMove r = SpecialMove.None;
        
        Chessboard cb = FindFirstObjectByType<Chessboard>();
        int enemyTeam = (team == 0) ? 1 : 0;
        int y = (team == 0) ? 0 : 7;

        bool kingMoved = moveList.Any(m => m[0].x == 4 && m[0].y == y);
        if (kingMoved || currentX != 4) return r;

        if (cb.IsSquareAttacked(new Vector2Int(4, y), enemyTeam)) return r;

        if (!moveList.Any(m => m[0].x == 7 && m[0].y == y))
        {
            if (board[7, y] != null && board[7, y].type == ChessPieceType.Rook && board[7, y].team == team)
            {
                if (board[5, y] == null && board[6, y] == null)
                {
                    if (!cb.IsSquareAttacked(new Vector2Int(5, y), enemyTeam) && 
                        !cb.IsSquareAttacked(new Vector2Int(6, y), enemyTeam))
                    {
                        availableMoves.Add(new Vector2Int(6, y));
                        r = SpecialMove.Castling;
                    }
                }
            }
        }

        if (!moveList.Any(m => m[0].x == 0 && m[0].y == y))
        {
            if (board[0, y] != null && board[0, y].type == ChessPieceType.Rook && board[0, y].team == team)
            {
                if (board[3, y] == null && board[2, y] == null && board[1, y] == null)
                {
                    if (!cb.IsSquareAttacked(new Vector2Int(3, y), enemyTeam) && 
                        !cb.IsSquareAttacked(new Vector2Int(2, y), enemyTeam))
                    {
                        availableMoves.Add(new Vector2Int(2, y));
                        r = SpecialMove.Castling;
                    }
                }
            }
        }

        return r;
    }
}