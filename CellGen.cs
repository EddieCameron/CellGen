/* CellGen.cs
 * Copyright Eddie Cameron 2012
 * ----------------------------
 * Holds info for a grid-based level, to be used in pathfinding & other game logic
 * Generates simple, varied, and guaranteed complete levels
 */
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TestSimpleRNG;

public class CellGen : MonoBehaviour
{
	public bool useRandomSeed = true;
	public int manualSeed = 0;	// or can set seed for repeating generation
	public float wallDensity = 0.5f;	// density of random walls (0 = no walls, 1 = max walls with connecting rooms)
	
	public float _areaSize = 30f;
	public float _cellSize = 3f;
	public int minRoomSize = 2;
	public int maxRoomSize = 5;
	
	public Transform[] wallPrefabs;
	public Transform wallEndPrefab;
	
	List<Cell> unassignedRooms = new List<Cell>();
	
	public static float areaSize{ get { return instance._areaSize; } }

	public static float cellSize{ get { return instance._cellSize; } }

	public static int cellsPerSide{ get { return cells.GetLength( 0 ); } }
	
	static Cell[,] cells;
	static CellGen instance;
	
	void Awake()
	{
		if ( instance )
		{
			// enforce singleton
			Destroy( this );
			return;
		}
		else
		{
			instance = this;
			
			manualSeed = Mathf.Max ( 0, manualSeed );
			
			// make sure area size is valid
			int numCells = Mathf.FloorToInt( areaSize / cellSize );
			_areaSize = numCells * cellSize;
			
			// populate cell array
			cells = new Cell[ numCells, numCells ];
			for ( int x = 0; x < numCells; x++ )
				for ( int y = 0; y < numCells; y++ )
				{
					Cell newCell = new Cell( x, y );
					// close cells on borders
					if ( x == 0 )
						newCell.canGoWest = false;
					if ( x == numCells - 1)
						newCell.canGoEast = false;
					if ( y == 0 )
						newCell.canGoSouth = false;
					if ( y == numCells - 1 )
						newCell.canGoNorth = false;
					
					cells[x,y] = newCell;
				}
		}
	}

	void Start()
	{
		GenWalls();
	}
	
	public static Cell GetCellAt( int x, int y )
	{
		if ( x < 0 || x >= cellsPerSide || y < 0 || y >= cellsPerSide )
		{
			Debug.LogWarning( "Invalid cell coordinate: ( " + x + ", " + y + " )" );
			return null;
		}
		return cells[x, y];
	}
		
	public static Cell GetCellAt( Vector3 pos )
	{
		int x = Mathf.Clamp( Mathf.FloorToInt( pos.x / cellSize + cellsPerSide * 0.5f ), 0, cellsPerSide - 1 );
		int y = Mathf.Clamp( Mathf.FloorToInt( pos.z / cellSize + cellsPerSide * 0.5f ), 0, cellsPerSide - 1 );
		if ( x >= 0 && x < cellsPerSide && y >= 0 && y < cellsPerSide )
			return cells[x, y];
		
		return null;
	}
	
	#region Room Gen
	void GenWalls()
	{	
		if ( useRandomSeed )
			SimpleRNG.SetSeedFromSystemTime();
		else
			SimpleRNG.SetSeed( (uint)manualSeed );
		
		foreach ( Cell c in cells )
			unassignedRooms.Add( c );
		
		Cell startRoom = null;
		int curRoom = 1;
		while ( unassignedRooms.Count > 0 )
		{
			// add room of random size
			int nextRoomSize = (int)( SimpleRNG.GetUniform() * ( maxRoomSize - minRoomSize ) ) + minRoomSize;
			
			Cell centreCell = unassignedRooms[(int)( SimpleRNG.GetUniform () * ( unassignedRooms.Count - 1 ) )];
			if ( startRoom == null )
				startRoom = centreCell;
			
			// work out ideal bounds of new room
			int startX = centreCell.x - Mathf.CeilToInt( nextRoomSize / 2f ) + 1;
			int endX = Mathf.Min( startX + nextRoomSize, cellsPerSide );
			startX = Mathf.Max( 0, startX );
			
			int startY = centreCell.y - Mathf.CeilToInt( nextRoomSize / 2f ) + 1;
			int endY = Mathf.Min( startY + nextRoomSize, cellsPerSide );
			startY = Mathf.Max( 0, startY );
			
			var roomCells = new List<Cell>();
			var lastInRoom = new List<int>();	// which rows in the last column had a room on it? If no rows match, column won't be inited and room will stop. Avoids split rooms
			
			for ( int x = startX; x < endX; x++ )
			{
				var cellsThisColumn = new List<int>();
				if ( lastInRoom.Count == 0 )
				{
					// no cells in room yet, add first block
					bool started = false;
					for ( int y = startY; y < endY; y++ )
					{
						if ( cells[x,y].room == 0 )
						{
							cellsThisColumn.Add ( y );
							started = true;
						}
						else if ( started )
							break;
					}
				}
				else
				{
					// add last column's rooms to this column if valid, then spread up and down until hits another room
					foreach ( int roomRow in lastInRoom )
					{
						if ( !cellsThisColumn.Contains( roomRow ) && cells[x, roomRow].room == 0 )
						{
							cellsThisColumn.Add( roomRow );
							for ( int south = roomRow - 1; south >= startY; south-- )
								if ( cells[x,south].room == 0 )
									cellsThisColumn.Add ( south );
								else
									break;
							for ( int north = roomRow + 1; north < endY; north++ )
								if ( cells[x,north].room == 0 )
									cellsThisColumn.Add ( north );
								else
									break;
						}
					}
					
					// if no valid connection after room has started, stop making room
					if ( cellsThisColumn.Count == 0 )
						break;
				}
				
				// actually make rooms
				foreach ( int row in cellsThisColumn )
				{
					// for each cell within room edges, add walls between neighbouring rooms (if not in another room already)
					// add each valid room to list, and if can't path to first room after all rooms done, make holes
					Cell roomCell = cells[x, row];
					if ( AddCellToRoom( roomCell, curRoom ) )
						roomCells.Add ( roomCell );
				}
				lastInRoom = cellsThisColumn;
			}
			
			Debug.Log( "Room made" );
			PrintLayout();
			
			// try to path to start room
			if ( roomCells.Count > 0 && CellPath.PathTo( startRoom.centrePosition, roomCells[0].centrePosition ) == null )
			{
				// no path, make corridor to first cell
				Cell pathEnd = null;
				int distToTarg = int.MaxValue;
				foreach ( Cell edgeCell in roomCells )
				{
					int newDist = Mathf.Abs( edgeCell.x - startRoom.x ) + Mathf.Abs( edgeCell.y - startRoom.y );
					if ( newDist < distToTarg )
					{
						distToTarg = newDist;
						pathEnd = edgeCell;
					}
				}
				
				while ( pathEnd.room == curRoom )
				{
					Debug.Log( "Opening path from " + pathEnd );
					int xDist = startRoom.x - pathEnd.x;
					int yDist = startRoom.y - pathEnd.y;
					if ( xDist >= Mathf.Abs( yDist ) )
						pathEnd = OpenCellInDirection( pathEnd, Direction.East );
					else if ( xDist <= -Mathf.Abs( yDist ) )
						pathEnd = OpenCellInDirection( pathEnd, Direction.West );
					else if ( yDist > Mathf.Abs( xDist ) )
						pathEnd = OpenCellInDirection( pathEnd, Direction.North );
					else if ( yDist < -Mathf.Abs( xDist ) )
						pathEnd = OpenCellInDirection( pathEnd, Direction.South );
				}		
				
				// check if can path. JUST IN CASE
				if ( CellPath.PathTo( startRoom.centrePosition, roomCells[0].centrePosition ) == null )
				{
					Debug.LogWarning( "Still no path from room " + curRoom );
					PrintLayout();
				}
			}
		
			curRoom++;
		}
		
		Debug.Log( "Layout complete..." );
		PrintLayout();
		
		// Instantiate walls?
		var verticalWalls = new Cell[cellsPerSide, cellsPerSide];
		for ( int x = 0; x < cellsPerSide - 1; x++ )
		{
			int wallType = Random.Range( 0, wallPrefabs.Length );
			for ( int y = 0; y < cellsPerSide; y++ )
				if ( !cells[x,y].canGoEast )
				{
					CreateWall( cells[x,y], Direction.East, wallType );
					verticalWalls[x,y] = cells[x,y];
					
					if ( y > 0 && verticalWalls[x,y - 1] == null )	
						CreateWallCap ( cells[x,y], true );
				}
				else
				{
					wallType = Random.Range ( 0, wallPrefabs.Length );
					if ( y > 0 && verticalWalls[x,y - 1] != null )
						CreateWallCap ( cells[x,y], true );
				}
		}
		
		var horizontalWalls = new Cell[cellsPerSide, cellsPerSide];
		for ( int y = 0; y < cellsPerSide - 1; y++ )
		{
			int wallType = Random.Range( 0, wallPrefabs.Length );
			for ( int x = 0; x < cellsPerSide; x++ )
				if ( !cells[x,y].canGoNorth )
				{
					CreateWall( cells[x,y], Direction.North, wallType );
					horizontalWalls[x,y] = cells[x,y];
					
					if ( x > 0 && horizontalWalls[x - 1,y] == null )	
						CreateWallCap ( cells[x,y], false );
				}
				else
				{
					wallType = Random.Range ( 0, wallPrefabs.Length );
					if ( x > 0 && horizontalWalls[x - 1,y] != null )
						CreateWallCap ( cells[x,y], false );
				}
		}
	}
	
	/// <summary>
	/// Opens the cell in direction.
	/// </summary>
	/// <param name='toOpen'>
	/// To open.
	/// </param>
	/// <param name='inDir'>
	/// In dir.
	/// </param>
	Cell OpenCellInDirection( Cell toOpen, Direction inDir )
	{
		Debug.Log( "Opening cell " + toOpen + " in direction " + inDir );
		Cell nextCell = toOpen.CellInDirection( inDir );
						
		if ( !toOpen.CanGoInDirection( inDir ) )
		{
			toOpen.SetOpenInDirection( inDir, true );
			if ( nextCell.room == 0 )
				Debug.LogWarning( "Wall made to uninitialised cell" );
			nextCell.SetOpenInDirection( Cell.Reverse( inDir ), true );
			// made connection to another room. Stop.
		}
		else
			// add uninited cell towards startCell to current room
			if ( nextCell.room != 0 )
			{
				Debug.LogWarning ( "No path to start, but can reach another room???" );
			}
			else
			{
				AddCellToRoom ( nextCell, toOpen.room );
			}
		return nextCell;
	}
	
	void PrintLayout()
	{
		/// Print room layout to console
		Debug.Log( "Room layout" );
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		for ( int y = cellsPerSide - 1; y >= 0; y-- )
		{
			for ( int x = 0; x < cellsPerSide; x++ )
				sb.Append( " " + ( cells[x, y].canGoNorth ? " " : "#" ) + " " );
			sb.AppendLine();
			for ( int x = 0; x < cellsPerSide; x++ )
				sb.Append( ( cells[x, y].canGoWest ? " " : "#" ) + cells[x, y].room.ToString( "d2" ) );
			sb.AppendLine();
		}
		Debug.Log( sb.ToString() );
		///	
	}
		
	/// <summary>
	/// Adds the cell to given room.
	/// </summary>
	/// <returns>
	/// Whether the cell was added
	/// </returns>
	/// <param name='newCell'>
	/// Cell to add
	/// </param>
	/// <param name='inRoom'>
	/// Room number to add to
	/// </param>
	bool AddCellToRoom( Cell newCell, int inRoom )
	{
		// Add walls between this and cells that have been set to other rooms
		if ( newCell.room == 0 )
		{
			newCell.room = inRoom;
			if ( newCell.x > 0 && newCell.CellInDirection( Direction.West ).room != 0 && newCell.CellInDirection( Direction.West ).room != inRoom && SimpleRNG.GetUniform() < wallDensity )
				newCell.canGoWest = newCell.CellInDirection( Direction.West ).canGoEast = false;
			if ( newCell.x < cellsPerSide - 1 && newCell.CellInDirection( Direction.East ).room != 0 && newCell.CellInDirection( Direction.East ).room != inRoom && SimpleRNG.GetUniform() < wallDensity )
				newCell.canGoEast = newCell.CellInDirection( Direction.East ).canGoWest = false;
			if ( newCell.y > 0 && newCell.CellInDirection( Direction.South ).room != 0 && newCell.CellInDirection( Direction.South ).room != inRoom && SimpleRNG.GetUniform() < wallDensity )
				newCell.canGoSouth = newCell.CellInDirection( Direction.South ).canGoNorth = false;
			if ( newCell.y < cellsPerSide - 1 && newCell.CellInDirection( Direction.North ).room != 0 && newCell.CellInDirection( Direction.North ).room != inRoom && SimpleRNG.GetUniform() < wallDensity )
				newCell.canGoNorth = newCell.CellInDirection( Direction.North ).canGoSouth = false;	
			
			unassignedRooms.Remove( newCell );
			return true;
		}
		
		return false;
	}
	
	/// <summary>
	/// Instantiates a wall between two cells.
	/// </summary>
	/// <param name='cellA'>
	/// Cell a.
	/// </param>
	/// <param name='cellB'>
	/// Cell b.
	/// </param>
	void CreateWall( Cell cellA, Direction inDirection, int prefabID )
	{
		Vector3 spawnPos = cellA.centrePosition;
		Quaternion spawnRot = Quaternion.identity;
		switch ( inDirection )
		{
		case Direction.North:
			spawnPos += Vector3.forward * cellSize * 0.5f;
			break;
		case Direction.South:
			spawnPos += Vector3.back * cellSize * 0.5f;
			break;
		case Direction.East:
			spawnPos += Vector3.right * cellSize * 0.5f;
			spawnRot = Quaternion.AngleAxis( 90, Vector3.up );
			break;
		case Direction.West:
			spawnPos += Vector3.left * cellSize * 0.5f;
			spawnRot = Quaternion.AngleAxis( 90, Vector3.up );
			break;
		}
		
		Instantiate( wallPrefabs[prefabID], spawnPos, spawnRot );
	}
	
	/// <summary>
	/// Creates a wall cap.
	/// </summary>
	/// <param name='onCell'>
	/// On cell.
	/// </param>
	/// <param name='southEast'>
	/// Whether cap is on south east corner of cell or north west.
	/// </param>
	void CreateWallCap( Cell onCell, bool southEast )
	{
		if ( southEast )
			Instantiate( wallEndPrefab, onCell.centrePosition + new Vector3( 0.5f, 0, -0.5f ) * cellSize, Quaternion.identity );
		else  
			Instantiate( wallEndPrefab, onCell.centrePosition + new Vector3( -0.5f, 0, 0.5f ) * cellSize, Quaternion.identity );
	}
	#endregion
	
	static Direction RandomDirection()
	{
		float a = Random.value;
		if ( a < 0.25f )
			return Direction.North;
		if ( a < 0.5f )
			return Direction.South;
		if ( a < 0.75f )
			return Direction.East;
		return Direction.West;
	}
	
	void OnDestroy()
	{
		if ( instance == this )
			instance = null;
	}
}

public class Cell
{
	public int x, y;
	public int room = 0;
	Vector3 _centrePosition;

	public Vector3 centrePosition {
		get {
			if ( _centrePosition.y != 0 )
				_centrePosition = new Vector3( CellGen.cellSize * ( x + 0.5f - CellGen.cellsPerSide * 0.5f ), 0, CellGen.cellSize * ( y + 0.5f - CellGen.cellsPerSide * 0.5f ) );
			
			return _centrePosition;
		}
	}
	
	public bool canGoNorth;
	public bool canGoEast;
	public bool canGoSouth;
	public bool canGoWest;
	
	public Cell()
	{
		x = y = -1;
		_centrePosition = -Vector3.one;
		canGoNorth = canGoSouth = canGoEast = canGoWest = true;
	}
	
	public Cell( int x, int y )
	{
		this.x = x;
		this.y = y;
		
		_centrePosition = new Vector3( CellGen.cellSize * ( x + 0.5f - CellGen.cellsPerSide * 0.5f ), 0, CellGen.cellSize * ( y + 0.5f - CellGen.cellsPerSide * 0.5f ) );
		canGoNorth = canGoSouth = canGoEast = canGoWest = true;
	}
						
	public bool CanGoInDirection( Direction dir )
	{
		switch ( dir )
		{
		case Direction.North:
			return canGoNorth;
		case Direction.South:
			return canGoSouth;
		case Direction.East:
			return canGoEast;
		case Direction.West:
			return canGoWest;
		default:
			Debug.LogWarning( "Invalid direction " + dir );
			return false;
		}
	}
	
	public void SetOpenInDirection( Direction dir, bool toOpen )
	{
		switch ( dir )
		{
		case Direction.North:
			canGoNorth = toOpen;
			break;
		case Direction.South:
			canGoSouth = toOpen;
			break;
		case Direction.East:
			canGoEast = toOpen;
			break;
		case Direction.West:
			canGoWest = toOpen;
			break;
		default:
			Debug.LogWarning( "Invalid direction " + dir );
			break;
		}
	}
	
	public Cell CellInDirection( Direction dir )
	{
		switch ( dir )
		{
		case Direction.North:
			if ( y < CellGen.cellsPerSide - 1 )
				return CellGen.GetCellAt( x, y + 1 );
			break;
		case Direction.South:
			if ( y > 0 )
				return CellGen.GetCellAt( x, y - 1 );
			break;
		case Direction.East:
			if ( x < CellGen.cellsPerSide - 1 )
				return CellGen.GetCellAt( x + 1, y );
			break;
		case Direction.West:
			if ( x > 0 )
				return CellGen.GetCellAt( x - 1, y );
			break;
		default:
			Debug.LogWarning( "Invalid direction " + dir );
			break;
		}
		return null;
	}
	
	public static Direction Reverse( Direction dir )
	{
		switch ( dir )
		{
		case Direction.North:
			return Direction.South;
		case Direction.South:
			return Direction.North;
		case Direction.East:
			return Direction.West;
		case Direction.West:
			return Direction.East;
		default:
			Debug.LogWarning( "Invalid direction " + dir );
			return Direction.None;
		}
	}
	
	public override bool Equals( object obj )
	{
		Cell otherCell = obj as Cell;
		return otherCell != null && Equals( this, otherCell );
	}
	
	public bool Equals( Cell other )
	{
		return other != null && Equals( this, other );
	}
	
	public static bool Equals( Cell a, Cell b )
	{
		return a.x == b.x && a.y == b.y;
	}
	
	public override int GetHashCode()
	{
		return x.GetHashCode() + y.GetHashCode();
	}
	
	public override string ToString()
	{
		return string.Format( "[Cell: x={0}, y={1}]", x, y );
	}
}

public enum Direction
{
	None,
	North,
	South,
	East,
	West
}
