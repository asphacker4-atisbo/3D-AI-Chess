using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Motor de ajedrez híbrido de alto rendimiento — 8 niveles de dificultad.
/// Niveles 1-2 : MCTS puro (errores intencionados, nivel principiante/casual).
/// Niveles 3-8 : Negamax + Alpha-Beta + LMR + Null Move + Transposition Table.
/// El nivel 8 alcanza profundidades de 10-12+ ply con todas las optimizaciones activas.
/// </summary>
public class ChessAI : MonoBehaviour
{
    public static ChessAI Instance { get; private set; }

    // ══════════════════════════════════════
    //  CONFIGURACIÓN POR NIVEL (8 niveles)
    // ══════════════════════════════════════

    // Nivel 1: MCTS 120 sims  — comete blunders graves
    // Nivel 2: MCTS 300 sims  — comete errores frecuentes
    // Nivel 3: Negamax 3 ply  — principiante sólido
    // Nivel 4: Negamax 4 ply  — intermedio
    // Nivel 5: Negamax 5 ply  — intermedio-avanzado
    // Nivel 6: Negamax 7 ply  — avanzado
    // Nivel 7: Negamax 9 ply  — experto / ELO ~2000
    // Nivel 8: Negamax ~8-10 ply en 3s — fuerte / ELO ~2200+

    private static readonly int[]  MCTSSimulations = { 0, 120,  300,  0,    0,    0,    0,    0,    0 };
    private static readonly int[]  NegamaxMaxDepth = { 0, 0,    0,    3,    4,    5,    7,    9,    16 };
    private static readonly int[]  TimeLimitMs     = { 0, 0,    0,    300,  500,  1000, 2000, 5000, 3000 };

    // Constantes del motor
    private const float MATE_SCORE       = 100000f;
    private const float DRAW_SCORE       = 0f;
    private const float C_PUCT           = 1.2f;
    private const int   TT_SIZE          = 1 << 20;   // ~1M entradas
    private const int   QUIESCENCE_DEPTH = 6;
    private const int   NULL_MOVE_R      = 2;
    private const int   LMR_MIN_DEPTH    = 3;
    private const int   LMR_MIN_MOVES    = 3;

    // ══════════════════════════════════════
    //  PSQT — PERSPECTIVA BLANCAS (fila 0 = rango 1)
    // ══════════════════════════════════════
    private static readonly int[,] PawnPST = {
        {  0,  0,  0,  0,  0,  0,  0,  0 },
        { 50, 50, 50, 50, 50, 50, 50, 50 },
        { 10, 10, 20, 30, 30, 20, 10, 10 },
        {  5,  5, 10, 27, 27, 10,  5,  5 },
        {  0,  0,  0, 25, 25,  0,  0,  0 },
        {  5, -5,-10,  0,  0,-10, -5,  5 },
        {  4, 10, 10,-20,-20, 10, 10,  4 },
        {  0,  0,  0,  0,  0,  0,  0,  0 }
    };
    private static readonly int[,] KnightPST = {
        {-50,-40,-30,-30,-30,-30,-40,-50},
        {-40,-20,  0,  0,  0,  0,-20,-40},
        {-30,  0, 10, 15, 15, 10,  0,-30},
        {-30,  5, 15, 20, 20, 15,  5,-30},
        {-30,  0, 15, 20, 20, 15,  0,-30},
        {-30,  5, 10, 15, 15, 10,  5,-30},
        {-40,-20,  0,  5,  5,  0,-20,-40},
        {-50,-40,-30,-30,-30,-30,-40,-50}
    };
    private static readonly int[,] BishopPST = {
        {-20,-10,-10,-10,-10,-10,-10,-20},
        {-10,  0,  0,  0,  0,  0,  0,-10},
        {-10,  0,  5, 10, 10,  5,  0,-10},
        {-10,  5,  5, 10, 10,  5,  5,-10},
        {-10,  0, 10, 10, 10, 10,  0,-10},
        {-10, 10, 10, 10, 10, 10, 10,-10},
        {-10,  5,  0,  0,  0,  0,  5,-10},
        {-20,-10,-10,-10,-10,-10,-10,-20}
    };
    private static readonly int[,] RookPST = {
        {  0,  0,  0,  0,  0,  0,  0,  0},
        {  5, 10, 10, 10, 10, 10, 10,  5},
        { -5,  0,  0,  0,  0,  0,  0, -5},
        { -5,  0,  0,  0,  0,  0,  0, -5},
        { -5,  0,  0,  0,  0,  0,  0, -5},
        { -5,  0,  0,  0,  0,  0,  0, -5},
        { -5,  0,  0,  0,  0,  0,  0, -5},
        {  0,  0,  0,  5,  5,  0,  0,  0}
    };
    private static readonly int[,] QueenPST = {
        {-20,-10,-10, -5, -5,-10,-10,-20},
        {-10,  0,  0,  0,  0,  0,  0,-10},
        {-10,  0,  5,  5,  5,  5,  0,-10},
        { -5,  0,  5,  5,  5,  5,  0, -5},
        {  0,  0,  5,  5,  5,  5,  0, -5},
        {-10,  5,  5,  5,  5,  5,  0,-10},
        {-10,  0,  5,  0,  0,  0,  0,-10},
        {-20,-10,-10, -5, -5,-10,-10,-20}
    };
    private static readonly int[,] KingMiddlePST = {
        {-30,-40,-40,-50,-50,-40,-40,-30},
        {-30,-40,-40,-50,-50,-40,-40,-30},
        {-30,-40,-40,-50,-50,-40,-40,-30},
        {-30,-40,-40,-50,-50,-40,-40,-30},
        {-20,-30,-30,-40,-40,-30,-30,-20},
        {-10,-20,-20,-20,-20,-20,-20,-10},
        { 20, 20,  0,  0,  0,  0, 20, 20},
        { 20, 30, 10,  0,  0, 10, 30, 20}
    };
    private static readonly int[,] KingEndgamePST = {
        {-50,-40,-30,-20,-20,-30,-40,-50},
        {-30,-20,-10,  0,  0,-10,-20,-30},
        {-30,-10, 20, 30, 30, 20,-10,-30},
        {-30,-10, 30, 40, 40, 30,-10,-30},
        {-30,-10, 30, 40, 40, 30,-10,-30},
        {-30,-10, 20, 30, 30, 20,-10,-30},
        {-30,-30,  0,  0,  0,  0,-30,-30},
        {-50,-30,-30,-30,-30,-30,-30,-50}
    };

    // ══════════════════════════════════════
    //  TRANSPOSITION TABLE
    // ══════════════════════════════════════
    private struct TTEntry
    {
        public ulong Hash;
        public float Score;
        public int   Depth;
        public byte  Flag;   // 0=exact, 1=lowerbound, 2=upperbound
        public sbyte BestFromX, BestFromY, BestToX, BestToY;
    }
    private static readonly TTEntry[] _tt = new TTEntry[TT_SIZE];

    // ══════════════════════════════════════
    //  KILLER MOVES & HISTORY HEURISTIC
    // ══════════════════════════════════════
    private Vector2Int[][] _killerMoves;    // [ply][slot*2] — guarda from+to
    private int[,,,]       _historyTable;   // [team][fromX][fromY][toX*8+toY]
    private readonly List<Vector2Int[]> _rawMoveBuffer = new(64);
    private readonly List<ScoredMove>   _scoredMoveBuffer = new(64);

    private struct MoveUndo
    {
        public ChessPiece     Captured;
        public ChessPieceType MovedType;        // Original type before search-time promotion
        public ChessPiece     EnPassantCapture; // Pawn removed for en passant
    }

    // ══════════════════════════════════════
    //  ZOBRIST HASHING
    // ══════════════════════════════════════
    private static readonly ulong[,,,] ZobristTable;  // [x][y][pieceIdx][team]
    private static readonly ulong      ZobristBlack;

    static ChessAI()
    {
        var rng = new System.Random(0x5EED_CAFE);
        ZobristTable = new ulong[8, 8, 7, 2];
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                for (int p = 0; p < 7; p++)
                    for (int t = 0; t < 2; t++)
                        ZobristTable[x, y, p, t] = RandUlong(rng);
        ZobristBlack = RandUlong(rng);
    }
    private static ulong RandUlong(System.Random r)
    {
        byte[] b = new byte[8]; r.NextBytes(b);
        return BitConverter.ToUInt64(b, 0);
    }

    // ══════════════════════════════════════
    //  UNITY LIFECYCLE
    // ══════════════════════════════════════
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ══════════════════════════════════════════════════════════
    //  PUNTO DE ENTRADA  — misma firma que el original
    // ══════════════════════════════════════════════════════════
    public async Task<Vector2Int[]> GetBestMoveAsync(ChessPiece[,] board, int aiTeam, int difficultyLevel)
    {
        difficultyLevel = Mathf.Clamp(difficultyLevel, 1, 8);

        // Niveles 1-2: MCTS con errores intencionados
        if (difficultyLevel <= 2)
            return await GetMoveViaMCTS(board, aiTeam, difficultyLevel);

        // Niveles 3-8: Negamax con Iterative Deepening
        return await GetMoveViaNegamax(board, aiTeam, difficultyLevel);
    }

    // ══════════════════════════════════════════════════════════
    //  MCTS — NIVELES 1 y 2 (principiante, comete errores)
    // ══════════════════════════════════════════════════════════
    private async Task<Vector2Int[]> GetMoveViaMCTS(ChessPiece[,] board, int aiTeam, int level)
    {
        int sims = MCTSSimulations[level];
        MCTSNode root = new MCTSNode(CloneBoard(board), null, null, aiTeam);

        await Task.Run(() =>
        {
            ExpandMCTS(root);
            for (int i = 0; i < sims; i++)
            {
                MCTSNode sel = SelectPUCT(root);
                float eval = QuickMaterialEval(sel.BoardState, aiTeam);
                if (sel.Children.Count == 0 && sel.VisitCount > 0)
                    ExpandMCTS(sel);
                BackpropMCTS(sel, eval);
            }
        });

        return root.Children
            .OrderByDescending(c => c.VisitCount)
            .FirstOrDefault()?.MoveThatLedHere;
    }

    // ══════════════════════════════════════════════════════════
    //  NEGAMAX + ITERATIVE DEEPENING — NIVELES 3-8
    // ══════════════════════════════════════════════════════════
    private async Task<Vector2Int[]> GetMoveViaNegamax(ChessPiece[,] board, int aiTeam, int level)
    {
        int maxDepth   = NegamaxMaxDepth[level];
        int timeLimitMs = TimeLimitMs[level];

        // Inicializar tablas heurísticas
        int killerLen = maxDepth + 6;
        _killerMoves  = new Vector2Int[killerLen][];
        for (int i = 0; i < killerLen; i++) _killerMoves[i] = new Vector2Int[4]; // slot0_from, slot0_to, slot1_from, slot1_to
        _historyTable = new int[2, 8, 8, 64];

        Vector2Int[] bestMove  = null;
        float        bestScore = -MATE_SCORE;

        var cts   = new CancellationTokenSource();
        var token = cts.Token;

        // Cancelar la búsqueda cuando se acabe el tiempo
        _ = Task.Run(async () =>
        {
            await Task.Delay(timeLimitMs);
            cts.Cancel();
        });

        await Task.Run(() =>
        {
            ChessPiece[,] searchBoard = CloneBoard(board);
            ulong rootHash = ComputeZobrist(searchBoard, aiTeam);

            // Copia propia: GetOrderedMoves reutiliza un buffer interno durante la búsqueda
            var rootMovesOrdered = GetOrderedMoves(searchBoard, aiTeam, 0, hash: rootHash);
            if (rootMovesOrdered.Count == 0) return;
            var rootMoves = new List<ScoredMove>(rootMovesOrdered);

            bestMove = rootMoves[0].Move;

            // Iterative Deepening: de 1 hasta maxDepth
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                if (token.IsCancellationRequested) break;

                // Aspiration windows para depth ≥ 3
                float alpha, beta;
                if (depth >= 3 && bestScore > -MATE_SCORE + 2000 && bestScore < MATE_SCORE - 2000)
                {
                    alpha = bestScore - 60f;
                    beta  = bestScore + 60f;
                }
                else
                {
                    alpha = -MATE_SCORE;
                    beta  =  MATE_SCORE;
                }

                float        iterBest     = -MATE_SCORE;
                Vector2Int[] iterBestMove = null;
                bool         failedWindow = false;

                do
                {
                    failedWindow = false;
                    iterBest     = -MATE_SCORE;
                    iterBestMove = null;

                    for (int mi = 0; mi < rootMoves.Count; mi++)
                    {
                        if (token.IsCancellationRequested) goto DONE;

                        var entry = rootMoves[mi];
                        ulong nextHash = UpdateZobrist(rootHash, searchBoard, entry.Move, aiTeam);
                        MakeMove(searchBoard, entry.Move, out var undo);

                        float score;
                        if (mi == 0)
                            score = -Search(searchBoard, depth - 1, -beta, -alpha, 1 - aiTeam, 1, nextHash, token);
                        else
                        {
                            score = -Search(searchBoard, depth - 1, -alpha - 1, -alpha, 1 - aiTeam, 1, nextHash, token);
                            if (score > alpha && score < beta)
                                score = -Search(searchBoard, depth - 1, -beta, -alpha, 1 - aiTeam, 1, nextHash, token);
                        }

                        UnmakeMove(searchBoard, entry.Move, undo);

                        if (score > iterBest)
                        {
                            iterBest     = score;
                            iterBestMove = entry.Move;
                        }
                        if (score > alpha) alpha = score;
                        if (alpha >= beta) break;
                    }

                    // Fallo de aspiration window → ventana completa y repetir
                    if (iterBest <= alpha - 60f || iterBest >= beta - 60f)
                    {
                        alpha = -MATE_SCORE;
                        beta  =  MATE_SCORE;
                        failedWindow = true;
                    }
                } while (failedWindow);

                DONE:
                if (!token.IsCancellationRequested && iterBestMove != null)
                {
                    bestScore = iterBest;
                    bestMove  = iterBestMove;
                    PromoteRootMove(rootMoves, iterBestMove);
                    Debug.Log($"[ChessAI Lvl{level}] Depth {depth} | Score {bestScore:F0} | {bestMove[0]}→{bestMove[1]}");
                }

                if (Mathf.Abs(bestScore) > MATE_SCORE - 1000) break; // Mate encontrado
            }
        }, CancellationToken.None);

        cts.Cancel();
        return bestMove;
    }

    // ══════════════════════════════════════════════════════════
    //  NEGAMAX ALPHA-BETA CON TODAS LAS OPTIMIZACIONES
    // ══════════════════════════════════════════════════════════
    private float Search(ChessPiece[,] board, int depth, float alpha, float beta,
                         int turn, int ply, ulong hash, CancellationToken token)
    {
        if (token.IsCancellationRequested) return 0f;

        // Detección de mate
        if (IsKingDead(board, 0)) return turn == 1 ?  (MATE_SCORE - ply) : -(MATE_SCORE - ply);
        if (IsKingDead(board, 1)) return turn == 0 ?  (MATE_SCORE - ply) : -(MATE_SCORE - ply);

        // ── TRANSPOSITION TABLE ───────────────────────────────
        int     ttIdx   = (int)((hash & 0x7FFFFFFF) % TT_SIZE);
        ref TTEntry tte = ref _tt[ttIdx];
        if (tte.Hash == hash && tte.Depth >= depth)
        {
            float ts = tte.Score;
            if (tte.Flag == 0) return ts;
            if (tte.Flag == 1 && ts > alpha) alpha = ts;
            if (tte.Flag == 2 && ts < beta)  beta  = ts;
            if (alpha >= beta) return ts;
        }

        // ── QUIESCENCE EN HOJA ────────────────────────────────
        if (depth == 0)
            return QuiescenceSearch(board, alpha, beta, QUIESCENCE_DEPTH, turn, hash);

        // ── NULL MOVE PRUNING ─────────────────────────────────
        bool endgame = IsEndgame(board);
        if (!endgame && depth >= 3 && ply > 0 && beta < MATE_SCORE - 1000)
        {
            float nullScore = -Search(board, depth - 1 - NULL_MOVE_R, -beta, -beta + 1,
                                      1 - turn, ply + 1, hash ^ ZobristBlack, token);
            if (nullScore >= beta) return beta;
        }

        // ── OBTENER Y ORDENAR MOVIMIENTOS ────────────────────
        var moves = GetOrderedMoves(board, turn, ply, hash);
        if (moves.Count == 0) return DRAW_SCORE;

        float        best          = -MATE_SCORE;
        byte         flag          = 2;
        int          moveCount     = 0;
        Vector2Int[] bestMoveLocal = null;

        foreach (var entry in moves)
        {
            if (token.IsCancellationRequested) return 0f;

            ulong nextHash = UpdateZobrist(hash, board, entry.Move, turn);
            MakeMove(board, entry.Move, out var undo);
            bool isCapture = entry.IsCapture;

            float score;

            // ── LATE MOVE REDUCTIONS (LMR) + PVS ─────────────────
            if (moveCount == 0)
            {
                score = -Search(board, depth - 1, -beta, -alpha, 1 - turn, ply + 1, nextHash, token);
            }
            else if (!isCapture && depth >= LMR_MIN_DEPTH && moveCount >= LMR_MIN_MOVES)
            {
                int r = 1;
                if (moveCount >= 6) r++;
                if (depth   >= 7)   r++;
                int reduced = Mathf.Max(depth - 1 - r, 0);

                score = -Search(board, reduced, -alpha - 1, -alpha, 1 - turn, ply + 1, nextHash, token);
                if (score > alpha && score < beta)
                    score = -Search(board, depth - 1, -beta, -alpha, 1 - turn, ply + 1, nextHash, token);
            }
            else
            {
                score = -Search(board, depth - 1, -alpha - 1, -alpha, 1 - turn, ply + 1, nextHash, token);
                if (score > alpha && score < beta)
                    score = -Search(board, depth - 1, -beta, -alpha, 1 - turn, ply + 1, nextHash, token);
            }

            UnmakeMove(board, entry.Move, undo);

            moveCount++;

            if (score > best)
            {
                best           = score;
                bestMoveLocal  = entry.Move;
            }
            if (score > alpha)
            {
                alpha = score;
                flag  = 0;

                // Actualizar history heuristic
                if (!isCapture && _historyTable != null)
                {
                    int toIdx = entry.Move[1].x * 8 + entry.Move[1].y;
                    if (_historyTable[turn, entry.Move[0].x, entry.Move[0].y, toIdx] < 1_000_000)
                        _historyTable[turn, entry.Move[0].x, entry.Move[0].y, toIdx] += depth * depth;
                }
            }

            if (alpha >= beta)
            {
                // Guardar killer move
                if (!isCapture && ply < _killerMoves.Length)
                {
                    _killerMoves[ply][2] = _killerMoves[ply][0];
                    _killerMoves[ply][3] = _killerMoves[ply][1];
                    _killerMoves[ply][0] = entry.Move[0];
                    _killerMoves[ply][1] = entry.Move[1];
                }
                bestMoveLocal = entry.Move;
                flag = 1;
                break;
            }
        }

        // Guardar en TT
        var ttMove = bestMoveLocal;
        _tt[ttIdx] = new TTEntry
        {
            Hash = hash, Score = best, Depth = depth, Flag = flag,
            BestFromX = ttMove != null ? (sbyte)ttMove[0].x : (sbyte)-1,
            BestFromY = ttMove != null ? (sbyte)ttMove[0].y : (sbyte)-1,
            BestToX   = ttMove != null ? (sbyte)ttMove[1].x : (sbyte)-1,
            BestToY   = ttMove != null ? (sbyte)ttMove[1].y : (sbyte)-1,
        };
        return best;
    }

    // ══════════════════════════════════════════════════════════
    //  QUIESCENCE SEARCH
    // ══════════════════════════════════════════════════════════
    private float QuiescenceSearch(ChessPiece[,] board, float alpha, float beta,
                                   int depth, int turn, ulong hash)
    {
        float standPat = StaticEval(board, turn, includeMobility: depth == QUIESCENCE_DEPTH);
        if (standPat >= beta) return beta;
        if (standPat > alpha) alpha = standPat;
        if (depth == 0)       return alpha;

        const float DELTA = 200f;

        // Allocate a fresh list here to avoid corrupting the shared buffer in recursive calls
        var allMoves = new List<Vector2Int[]>(GetAllRawMoves(board, turn));
        var localScored = new List<ScoredMove>(allMoves.Count);
        for (int i = 0; i < allMoves.Count; i++)
        {
            var move = allMoves[i];
            ChessPiece vic = board[move[1].x, move[1].y];
            if (vic == null) continue;
            ChessPiece att = board[move[0].x, move[0].y];
            localScored.Add(new ScoredMove
            {
                Move = move, IsCapture = true,
                Score = PieceVal(vic.type) * 10 - PieceVal(att.type)
            });
        }
        localScored.Sort((a, b) => b.Score.CompareTo(a.Score));

        for (int ci = 0; ci < localScored.Count; ci++)
        {
            var move = localScored[ci].Move;
            ChessPiece vic = board[move[1].x, move[1].y];

            // Delta pruning
            if (standPat + PieceVal(vic.type) + DELTA < alpha) continue;

            ulong nextHash = UpdateZobrist(hash, board, move, turn);
            MakeMove(board, move, out var undo);
            float score = -QuiescenceSearch(board, -beta, -alpha, depth - 1, 1 - turn, nextHash);
            UnmakeMove(board, move, undo);

            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }
        return alpha;
    }

    // ══════════════════════════════════════════════════════════
    //  EVALUACIÓN ESTÁTICA
    // ══════════════════════════════════════════════════════════
    private float StaticEval(ChessPiece[,] board, int side, bool includeMobility)
    {
        if (IsKingDead(board, 0)) return side == 1 ?  MATE_SCORE : -MATE_SCORE;
        if (IsKingDead(board, 1)) return side == 0 ?  MATE_SCORE : -MATE_SCORE;

        bool endgame = IsEndgame(board);
        float score = 0f;

        int[] pawnsPerFileW = new int[8];
        int[] pawnsPerFileB = new int[8];
        int[] lastWPawnRank = new int[8];
        int[] lastBPawnRank = new int[8];
        for (int i = 0; i < 8; i++) { lastWPawnRank[i] = -1; lastBPawnRank[i] = 8; }

        int wBishops = 0, bBishops = 0;

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                ChessPiece p = board[x, y];
                if (p == null) continue;

                int pv      = PieceVal(p.type);
                int tableY  = p.team == 0 ? y : 7 - y;
                int psqt    = GetPSQT(p.type, tableY, x, endgame);

                float pieceScore = pv + psqt;
                score += p.team == side ? pieceScore : -pieceScore;

                if (p.type == ChessPieceType.Pawn)
                {
                    if (p.team == 0)
                    {
                        pawnsPerFileW[x]++;
                        if (y > lastWPawnRank[x]) lastWPawnRank[x] = y;
                    }
                    else
                    {
                        pawnsPerFileB[x]++;
                        if (y < lastBPawnRank[x]) lastBPawnRank[x] = y;
                    }
                }
                if (p.type == ChessPieceType.Bishop)
                { if (p.team == 0) wBishops++; else bBishops++; }
            }
        }

        if (wBishops >= 2) score += (side == 0 ?  30f : -30f);
        if (bBishops >= 2) score += (side == 1 ?  30f : -30f);

        for (int f = 0; f < 8; f++)
        {
            if (pawnsPerFileW[f] > 1) score += (side == 0 ? -20f : 20f) * (pawnsPerFileW[f] - 1);
            if (pawnsPerFileB[f] > 1) score += (side == 1 ? -20f : 20f) * (pawnsPerFileB[f] - 1);

            bool wLeft  = f > 0 && pawnsPerFileW[f-1] > 0;
            bool wRight = f < 7 && pawnsPerFileW[f+1] > 0;
            if (pawnsPerFileW[f] > 0 && !wLeft && !wRight) score += (side == 0 ? -15f : 15f);

            bool bLeft  = f > 0 && pawnsPerFileB[f-1] > 0;
            bool bRight = f < 7 && pawnsPerFileB[f+1] > 0;
            if (pawnsPerFileB[f] > 0 && !bLeft && !bRight) score += (side == 1 ? -15f : 15f);

            if (lastWPawnRank[f] >= 0)
            {
                int wRank = lastWPawnRank[f];
                bool passed = true;
                for (int adj = Mathf.Max(0, f-1); adj <= Mathf.Min(7, f+1); adj++)
                {
                    if (pawnsPerFileB[adj] > 0 && lastBPawnRank[adj] < wRank)
                    { passed = false; break; }
                }
                if (passed) score += (side == 0 ? 1f : -1f) * (20f + wRank * 10f);
            }
            if (lastBPawnRank[f] < 8)
            {
                int bRank = 7 - lastBPawnRank[f];
                bool passed = true;
                for (int adj = Mathf.Max(0, f-1); adj <= Mathf.Min(7, f+1); adj++)
                {
                    if (pawnsPerFileW[adj] > 0 && lastWPawnRank[adj] > 7 - lastBPawnRank[f])
                    { passed = false; break; }
                }
                if (passed) score += (side == 1 ? 1f : -1f) * (20f + bRank * 10f);
            }
        }

        if (includeMobility)
        {
            int mobUs   = GetAllRawMoves(board, side).Count;
            int mobThem = GetAllRawMoves(board, 1 - side).Count;
            score += (mobUs - mobThem) * 3f;
        }

        return score;
    }

    private int GetPSQT(ChessPieceType type, int tableY, int x, bool endgame) => type switch
    {
        ChessPieceType.Pawn   => PawnPST[tableY, x],
        ChessPieceType.Knight => KnightPST[tableY, x],
        ChessPieceType.Bishop => BishopPST[tableY, x],
        ChessPieceType.Rook   => RookPST[tableY, x],
        ChessPieceType.Queen  => QueenPST[tableY, x],
        ChessPieceType.King   => endgame ? KingEndgamePST[tableY, x] : KingMiddlePST[tableY, x],
        _                     => 0
    };

    private float QuickMaterialEval(ChessPiece[,] board, int side)
    {
        float score = 0f;
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            {
                ChessPiece p = board[x, y];
                if (p == null) continue;
                float v = PieceVal(p.type);
                score += p.team == side ? v : -v;
            }
        return score / 20000f; // normalizado [-1,1] para MCTS
    }

    private bool IsEndgame(ChessPiece[,] board)
    {
        int queens = 0, total = 0;
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            {
                if (board[x, y] == null) continue;
                total++;
                if (board[x, y].type == ChessPieceType.Queen) queens++;
            }
        return queens == 0 || total <= 14;
    }

    // ══════════════════════════════════════════════════════════
    //  ORDENAMIENTO DE MOVIMIENTOS
    // ══════════════════════════════════════════════════════════
    private struct ScoredMove
    {
        public Vector2Int[] Move;
        public float        Score;
        public bool         IsCapture;
    }

    private List<ScoredMove> GetOrderedMoves(ChessPiece[,] board, int team, int ply, ulong hash)
    {
        var raw     = GetAllRawMoves(board, team);
        var scored  = _scoredMoveBuffer;
        scored.Clear();
        var killers = (ply < _killerMoves?.Length) ? _killerMoves[ply] : null;

        sbyte ttFx = -1, ttFy = -1, ttTx = -1, ttTy = -1;
        int ttIdx = (int)((hash & 0x7FFFFFFF) % TT_SIZE);
        ref TTEntry ttRef = ref _tt[ttIdx];
        if (ttRef.Hash == hash && ttRef.BestFromX >= 0)
        {
            ttFx = ttRef.BestFromX; ttFy = ttRef.BestFromY;
            ttTx = ttRef.BestToX;   ttTy = ttRef.BestToY;
        }

        foreach (var move in raw)
        {
            ChessPiece att = board[move[0].x, move[0].y];
            ChessPiece vic = board[move[1].x, move[1].y];
            bool isCapture = vic != null;
            float score    = 0f;

            if (isCapture)
            {
                score = 10000f + PieceVal(vic.type) * 10 - PieceVal(att.type);
            }
            else
            {
                if (ttFx >= 0 && move[0].x == ttFx && move[0].y == ttFy &&
                    move[1].x == ttTx && move[1].y == ttTy)
                    score = 9500f;

                if (killers != null && killers.Length >= 4)
                {
                    if (move[0] == killers[0] && move[1] == killers[1]) score = Mathf.Max(score, 9000f);
                    else if (move[0] == killers[2] && move[1] == killers[3]) score = Mathf.Max(score, 8900f);
                }

                if (_historyTable != null)
                {
                    int toIdx = move[1].x * 8 + move[1].y;
                    score += _historyTable[team, move[0].x, move[0].y, toIdx];
                }

                if (att.type == ChessPieceType.Pawn && (move[1].y == 7 || move[1].y == 0))
                    score = Mathf.Max(score, 8500f);
            }

            scored.Add(new ScoredMove { Move = move, Score = score, IsCapture = isCapture });
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return new List<ScoredMove>(scored);
    }

    // ══════════════════════════════════════════════════════════
    //  ZOBRIST HASHING
    // ══════════════════════════════════════════════════════════
    private ulong ComputeZobrist(ChessPiece[,] board, int turn)
    {
        ulong h = 0;
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            {
                ChessPiece p = board[x, y];
                if (p != null)
                    h ^= ZobristTable[x, y, PieceIdx(p.type), p.team];
            }
        if (turn == 1) h ^= ZobristBlack;
        return h;
    }

    private ulong UpdateZobrist(ulong hash, ChessPiece[,] board, Vector2Int[] move, int turn)
    {
        ChessPiece src = board[move[0].x, move[0].y];
        ChessPiece dst = board[move[1].x, move[1].y];
        if (src != null)
        {
            hash ^= ZobristTable[move[0].x, move[0].y, PieceIdx(src.type), src.team];
            hash ^= ZobristTable[move[1].x, move[1].y, PieceIdx(src.type), src.team];
        }
        if (dst != null)
            hash ^= ZobristTable[move[1].x, move[1].y, PieceIdx(dst.type), dst.team];
        hash ^= ZobristBlack;
        return hash;
    }

    private static int PieceIdx(ChessPieceType t) => t switch
    {
        ChessPieceType.Pawn   => 0,
        ChessPieceType.Knight => 1,
        ChessPieceType.Bishop => 2,
        ChessPieceType.Rook   => 3,
        ChessPieceType.Queen  => 4,
        ChessPieceType.King   => 5,
        _                     => 6
    };

    // ══════════════════════════════════════════════════════════
    //  MCTS AUXILIAR
    // ══════════════════════════════════════════════════════════
    private MCTSNode SelectPUCT(MCTSNode node)
    {
        MCTSNode cur = node;
        while (cur.Children.Count > 0)
        {
            float sqrtN = Mathf.Sqrt(cur.VisitCount + 1f);
            cur = cur.Children
                .OrderByDescending(c => -c.Value() + C_PUCT * c.Prior * sqrtN / (1f + c.VisitCount))
                .First();
        }
        return cur;
    }

    private void ExpandMCTS(MCTSNode node)
    {
        var moves = GetAllRawMoves(node.BoardState, node.TeamTurn);
        if (moves.Count == 0) return;

        float[] scores = new float[moves.Count];
        float   total  = 0f;
        for (int i = 0; i < moves.Count; i++)
        {
            scores[i] = MoveHeuristic(node.BoardState, moves[i]);
            total += scores[i];
        }
        for (int i = 0; i < moves.Count; i++)
        {
            MCTSNode child = new MCTSNode(ApplyMove(node.BoardState, moves[i]), node, moves[i], 1 - node.TeamTurn);
            child.Prior = total > 0 ? scores[i] / total : 1f / moves.Count;
            node.Children.Add(child);
        }
    }

    private void BackpropMCTS(MCTSNode node, float value)
    {
        MCTSNode cur = node;
        while (cur != null) { cur.VisitCount++; cur.TotalValue += value; value = -value; cur = cur.Parent; }
    }

    private float MoveHeuristic(ChessPiece[,] board, Vector2Int[] move)
    {
        float score = 10f;
        ChessPiece att = board[move[0].x, move[0].y];
        ChessPiece vic = board[move[1].x, move[1].y];
        if (vic != null) score += PieceVal(vic.type) * 10 - PieceVal(att.type) + 1000f;
        if (att.type == ChessPieceType.Pawn && (move[1].y == 7 || move[1].y == 0)) score += 800f;
        return score;
    }

    // ══════════════════════════════════════════════════════════
    //  HELPERS TABLERO
    // ══════════════════════════════════════════════════════════
    private int PieceVal(ChessPieceType t) => t switch
    {
        ChessPieceType.Pawn   => 100,
        ChessPieceType.Knight => 320,
        ChessPieceType.Bishop => 330,
        ChessPieceType.Rook   => 500,
        ChessPieceType.Queen  => 975,
        ChessPieceType.King   => 20000,
        _                     => 0
    };

    private List<Vector2Int[]> GetAllRawMoves(ChessPiece[,] board, int team)
    {
        _rawMoveBuffer.Clear();
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            {
                ChessPiece p = board[x, y];
                if (p == null || p.team != team) continue;
                var targets = p.GetAvailableMoves(ref board, 8, 8);
                if (targets == null) continue;
                foreach (var t in targets)
                    _rawMoveBuffer.Add(new[] { new Vector2Int(x, y), t });
            }
        return _rawMoveBuffer;
    }

    private static void MakeMove(ChessPiece[,] board, Vector2Int[] move, out MoveUndo undo)
    {
        ChessPiece moving = board[move[0].x, move[0].y];
        undo.Captured        = board[move[1].x, move[1].y];
        undo.MovedType        = moving?.type ?? ChessPieceType.None;
        undo.EnPassantCapture = null;

        board[move[1].x, move[1].y] = moving;
        board[move[0].x, move[0].y] = null;

        if (moving != null)
        {
            moving.currentX = move[1].x;
            moving.currentY = move[1].y;

            if (moving.type == ChessPieceType.Pawn)
            {
                // En passant: diagonal pawn capture onto an empty square
                if (move[0].x != move[1].x && undo.Captured == null)
                {
                    undo.EnPassantCapture = board[move[1].x, move[0].y];
                    board[move[1].x, move[0].y] = null;
                }
                // Promotion: temporarily treat the pawn as a queen for evaluation
                else if ((moving.team == 0 && move[1].y == 7) ||
                         (moving.team == 1 && move[1].y == 0))
                {
                    moving.type = ChessPieceType.Queen;
                }
            }
        }
    }

    private static void UnmakeMove(ChessPiece[,] board, Vector2Int[] move, in MoveUndo undo)
    {
        ChessPiece moved = board[move[1].x, move[1].y];
        board[move[0].x, move[0].y] = moved;
        board[move[1].x, move[1].y] = undo.Captured;

        if (moved != null)
        {
            moved.currentX = move[0].x;
            moved.currentY = move[0].y;
            moved.type     = undo.MovedType; // Restore type in case of promotion swap
        }
        if (undo.Captured != null)
        {
            undo.Captured.currentX = move[1].x;
            undo.Captured.currentY = move[1].y;
        }
        // Restore en-passant captured pawn
        if (undo.EnPassantCapture != null)
        {
            board[move[1].x, move[0].y]       = undo.EnPassantCapture;
            undo.EnPassantCapture.currentX = move[1].x;
            undo.EnPassantCapture.currentY = move[0].y;
        }
    }

    private static void PromoteRootMove(List<ScoredMove> rootMoves, Vector2Int[] best)
    {
        for (int i = 1; i < rootMoves.Count; i++)
        {
            var m = rootMoves[i].Move;
            if (m[0] == best[0] && m[1] == best[1])
            {
                var tmp = rootMoves[i];
                rootMoves[i] = rootMoves[0];
                rootMoves[0] = tmp;
                break;
            }
        }
    }

    private ChessPiece[,] ApplyMove(ChessPiece[,] board, Vector2Int[] move)
    {
        ChessPiece[,] next = CloneBoard(board);
        next[move[1].x, move[1].y] = next[move[0].x, move[0].y];
        next[move[0].x, move[0].y] = null;
        return next;
    }

    private ChessPiece[,] CloneBoard(ChessPiece[,] src)
    {
        ChessPiece[,] clone = new ChessPiece[8, 8];
        Array.Copy(src, clone, src.Length);
        return clone;
    }

    private bool IsKingDead(ChessPiece[,] board, int team)
    {
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                if (board[x, y]?.type == ChessPieceType.King && board[x, y].team == team)
                    return false;
        return true;
    }
}

// ══════════════════════════════════════════════════════════
//  NODO MCTS  (solo para niveles 1-2)
// ══════════════════════════════════════════════════════════
public class MCTSNode
{
    public ChessPiece[,]   BoardState;
    public MCTSNode        Parent;
    public List<MCTSNode>  Children        = new List<MCTSNode>();
    public int             VisitCount      = 0;
    public float           TotalValue      = 0f;
    public float           Prior           = 0f;
    public Vector2Int[]    MoveThatLedHere;
    public int             TeamTurn;

    public float Value() => VisitCount == 0 ? 0f : TotalValue / VisitCount;

    public MCTSNode(ChessPiece[,] board, MCTSNode parent, Vector2Int[] move, int turn)
    {
        BoardState      = board;
        Parent          = parent;
        MoveThatLedHere = move;
        TeamTurn        = turn;
    }
}