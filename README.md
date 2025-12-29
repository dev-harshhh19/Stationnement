# Stationnement - Smart Parking Management System

A full-stack parking reservation and management system built with **ASP.NET Core 8** and **Razor Pages**.

![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Neon-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

### For Users

- **Real-time Slot Availability** - Browse available parking slots across multiple locations
- **Easy Booking** - Select date, time, and vehicle type to book parking
- **Dynamic Pricing** - View pricing with discounts based on subscription plans
- **Reservation Management** - View, modify, or cancel reservations
- **Cancellation with Refund** - Cancel reservations with 10% fee, choose refund method (UPI/Bank/Wallet)
- **Barcode Check-in/out** - Seamless entry and exit with scannable barcodes
- **Payment History** - Track all transactions and download receipts

### For Admins

- **Dashboard Analytics** - View booking statistics and revenue
- **Location Management** - Add/edit parking locations and slots
- **User Management** - Manage user accounts and subscriptions
- **Barcode Scanner** - Camera-based or manual barcode verification
- **Audit Logs** - Track all system activities

## Tech Stack

| Layer        | Technology                           |
| ------------ | ------------------------------------ |
| **Backend**  | ASP.NET Core 8, C#                   |
| **Frontend** | Razor Pages, TailwindCSS, JavaScript |
| **Database** | PostgreSQL (Neon)                    |
| **ORM**      | Dapper                               |
| **Auth**     | JWT Bearer Tokens                    |
| **Payments** | UPI QR Code Integration              |

## Project Structure

```
Stationnement.Web/
├── Controllers/         # API endpoints
├── Models/              # Data models
├── Pages/               # Razor Pages UI
│   ├── Auth/            # Login, Register
│   ├── Dashboard/       # User dashboard
│   └── Admin/           # Admin panel
├── Repositories/        # Data access layer
├── Services/            # Business logic
├── wwwroot/             # Static assets (CSS, JS, images)
└── Program.cs           # Application entry point
```

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- PostgreSQL database (local or [Neon](https://neon.tech))

### Setup

1. **Clone the repository**

   ```bash
   git clone https://github.com/dev-harshhh19/stationnement.git
   cd stationnement/Stationnement.Web
   ```

2. **Configure environment variables**

   Create a `.env` file in the project root with your credentials:

   ```env
   DATABASE_URL=Host=your-host;Database=your-db;Username=your-user;Password=your-pass
   JWT_SECRET=your-secret-key-min-32-characters
   UPI_ID=your-upi-id@bank
   MERCHANT_NAME=Your Name
   ```

3. **Run the application**

   ```bash
   dotnet run
   ```

   Or with hot reload:

   ```bash
   dotnet watch run
   ```

4. **Open in browser**

   ```
   http://localhost:5283
   ```

## Configuration

### Environment Variables

| Variable       | Description                      |
| -------------- | -------------------------------- |
| `DATABASE_URL` | PostgreSQL connection string     |
| `JWT_SECRET`   | Secret key for JWT tokens (32+) |
| `UPI_ID`       | Merchant UPI ID for payments     |
| `MERCHANT_NAME`| Display name on payment QR       |
| `SMTP_HOST`    | Email SMTP server (optional)     |
| `SMTP_USERNAME`| SMTP username (optional)         |
| `SMTP_PASSWORD`| SMTP password (optional)         |

### Security Note

The `appsettings.json` file contains only non-sensitive defaults. All credentials must be placed in the `.env` file which is excluded from version control via `.gitignore`.

## API Endpoints

### Authentication

| Method | Endpoint             | Description       |
| ------ | -------------------- | ----------------- |
| POST   | `/api/auth/register` | Register new user |
| POST   | `/api/auth/login`    | Login user        |
| POST   | `/api/auth/refresh`  | Refresh JWT token |
| GET    | `/api/auth/me`       | Get current user  |

### Reservations

| Method | Endpoint                       | Description             |
| ------ | ------------------------------ | ----------------------- |
| GET    | `/api/reservation`             | Get user's reservations |
| POST   | `/api/reservation`             | Create reservation      |
| POST   | `/api/reservation/{id}/cancel` | Cancel reservation      |
| GET    | `/api/reservation/verify/{code}` | Verify barcode        |

### Parking

| Method | Endpoint                          | Description         |
| ------ | --------------------------------- | ------------------- |
| GET    | `/api/parking/locations`          | List all locations  |
| GET    | `/api/parking/slots/{locationId}` | Get available slots |

### Payments

| Method | Endpoint                    | Description           |
| ------ | --------------------------- | --------------------- |
| GET    | `/api/payment`              | Get payment history   |
| GET    | `/api/payment/receipt/{id}` | Download receipt      |
| GET    | `/api/payment/barcode/{id}` | Get printable barcode |

## License

This project is licensed under the MIT License.

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## Developer

**Harshad Nikam**

[![GitHub](https://img.shields.io/badge/GitHub-dev--harshhh19-181717?logo=github)](https://github.com/dev-harshhh19/)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-harshad--nikam06-0A66C2?logo=linkedin)](https://www.linkedin.com/in/harshad-nikam06/)
[![Twitter](https://img.shields.io/badge/X-@not__harshad__19-000000?logo=x)](https://x.com/not_harshad_19/)
[![Instagram](https://img.shields.io/badge/Instagram-dev.harshhh19-E4405F?logo=instagram)](https://www.instagram.com/dev.harshhh19/)
[![Email](https://img.shields.io/badge/Email-nikamharshadshivaji@gmail.com-D14836?logo=gmail)](mailto:nikamharshadshivaji@gmail.com)

---

Built with ❤️ using ASP.NET Core