using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite("Data Source=flights.db"));
var app = builder.Build();

// Create DB and seed sample flights
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    if (!db.Flights.Any())
    {
        var d = DateTime.Today.AddDays(1);
        db.Flights.AddRange(
            new Flight { Airline = "IndiGo 6E-201",     From = "Madurai",   To = "Chennai",   Departure = d.AddHours(6),  Price = 3200, Seats = 40 },
            new Flight { Airline = "Air India AI-544",  From = "Madurai",   To = "Chennai",   Departure = d.AddHours(14), Price = 4100, Seats = 25 },
            new Flight { Airline = "IndiGo 6E-512",     From = "Chennai",   To = "Delhi",     Departure = d.AddHours(8),  Price = 6800, Seats = 60 },
            new Flight { Airline = "Vistara UK-832",    From = "Chennai",   To = "Mumbai",    Departure = d.AddHours(11), Price = 5400, Seats = 35 },
            new Flight { Airline = "SpiceJet SG-118",   From = "Madurai",   To = "Bengaluru", Departure = d.AddHours(9),  Price = 2900, Seats = 30 },
            new Flight { Airline = "Air India AI-101",  From = "Delhi",     To = "Mumbai",    Departure = d.AddHours(19), Price = 5200, Seats = 50 });
        db.SaveChanges();
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();

// GET /api/flights?from=&to=&date=yyyy-MM-dd
app.MapGet("/api/flights", async (AppDb db, string? from, string? to, DateTime? date) =>
{
    var q = db.Flights.AsQueryable();
    if (!string.IsNullOrWhiteSpace(from)) q = q.Where(f => f.From.ToLower() == from.ToLower());
    if (!string.IsNullOrWhiteSpace(to))   q = q.Where(f => f.To.ToLower() == to.ToLower());
    var list = await q.OrderBy(f => f.Departure).ToListAsync();
    if (date.HasValue) list = list.Where(f => f.Departure.Date == date.Value.Date).ToList();
    return Results.Ok(list);
});

// POST /api/bookings
app.MapPost("/api/bookings", async (AppDb db, BookingRequest r) =>
{
    if (string.IsNullOrWhiteSpace(r.PassengerName) || string.IsNullOrWhiteSpace(r.Email) || r.Seats < 1)
        return Results.BadRequest(new { error = "Name, email and at least 1 seat are required." });

    var flight = await db.Flights.FindAsync(r.FlightId);
    if (flight is null) return Results.NotFound(new { error = "Flight not found." });
    if (flight.Seats < r.Seats) return Results.BadRequest(new { error = $"Only {flight.Seats} seats left." });

    flight.Seats -= r.Seats;
    var booking = new Booking
    {
        FlightId = flight.Id,
        PassengerName = r.PassengerName.Trim(),
        Email = r.Email.Trim().ToLower(),
        Seats = r.Seats,
        Total = flight.Price * r.Seats,
        Reference = "FB" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
        BookedAt = DateTime.Now
    };
    db.Bookings.Add(booking);
    await db.SaveChangesAsync();
    return Results.Created($"/api/bookings/{booking.Id}", booking);
});

// GET /api/bookings?email=
app.MapGet("/api/bookings", async (AppDb db, string email) =>
{
    var e = email.Trim().ToLower();
    var data = await db.Bookings.Where(b => b.Email == e)
        .Join(db.Flights, b => b.FlightId, f => f.Id, (b, f) => new
        { b.Id, b.Reference, b.PassengerName, b.Seats, b.Total, b.BookedAt, f.Airline, f.From, f.To, f.Departure })
        .OrderByDescending(x => x.BookedAt).ToListAsync();
    return Results.Ok(data);
});

// DELETE /api/bookings/{id}  (cancel and free the seats)
app.MapDelete("/api/bookings/{id:int}", async (AppDb db, int id) =>
{
    var b = await db.Bookings.FindAsync(id);
    if (b is null) return Results.NotFound();
    var f = await db.Flights.FindAsync(b.FlightId);
    if (f != null) f.Seats += b.Seats;
    db.Bookings.Remove(b);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

// ---------- Models ----------
public class Flight
{
    public int Id { get; set; }
    public string Airline { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public DateTime Departure { get; set; }
    public decimal Price { get; set; }
    public int Seats { get; set; }
}
public class Booking
{
    public int Id { get; set; }
    public int FlightId { get; set; }
    public string PassengerName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Seats { get; set; }
    public decimal Total { get; set; }
    public string Reference { get; set; } = "";
    public DateTime BookedAt { get; set; }
}
public record BookingRequest(int FlightId, string PassengerName, string Email, int Seats);

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> o) : base(o) { }
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<Booking> Bookings => Set<Booking>();
}
