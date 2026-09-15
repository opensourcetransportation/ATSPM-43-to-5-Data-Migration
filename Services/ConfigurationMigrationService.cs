#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Services/ConfigurationMigrationService.cs
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using DataMigrator.Commands;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;

namespace DataMigrator.Services;

public class ConfigurationMigrationService : IConfigurationMigrationService
{
    private const int SourceQueryTimeoutSeconds = 300;

    private readonly ILogger<ConfigurationMigrationService> _logger;
    private readonly IJurisdictionRepository _jurisdictionRepository;
    private readonly ILocationTypeRepository _locationTypeRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IApproachRepository _approachRepository;
    private readonly IDetectorRepository _detectorRepository;
    private readonly IDeviceConfigurationRepository _deviceConfigurationRepository;
    private readonly IProductRepository _productRepository;
    private readonly IRegionsRepository _regionsRepository;
    private readonly IAreaRepository _areaRepository;
    private readonly IDetectionTypeRepository _detectionTypeRepository;
    private readonly IMeasureTypeRepository _measureTypeRepository;
    private readonly IRouteRepository _routeRepository;
    private readonly IRouteLocationsRepository _routeLocationsRepository;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IConfiguration _applicationConfiguration;
    private TransferConfigCommandConfiguration _config = new();
    private CancellationToken _cancellationToken;
    private IReadOnlyDictionary<string, int>? _latestLocationIds;
    internal Func<string, CancellationToken, Task> SourcePreflightAsync { get; set; } = ValidateSourceAsync;

    public ConfigurationMigrationService(
        ILogger<ConfigurationMigrationService> logger,
        IJurisdictionRepository jurisdictionRepository,
        ILocationTypeRepository locationTypeRepository,
        ILocationRepository locationRepository,
        IApproachRepository approachRepository,
        IDetectorRepository detectorRepository,
        IDeviceRepository deviceRepository,
        IDeviceConfigurationRepository deviceConfigurationRepository,
        IProductRepository productRepository,
        IRegionsRepository regionsRepository,
        IAreaRepository areaRepository,
        IDetectionTypeRepository detectionTypeRepository,
        IMeasureTypeRepository measureTypeRepository,
        IRouteRepository routeRepository,
        IRouteLocationsRepository routeLocationsRepository,
        IServiceProvider serviceProvider,
        IConfiguration? applicationConfiguration = null
        )
    {
        _logger = logger;
        _jurisdictionRepository = jurisdictionRepository;
        _locationTypeRepository = locationTypeRepository;
        _locationRepository = locationRepository;
        _approachRepository = approachRepository;
        _detectorRepository = detectorRepository;
        _deviceConfigurationRepository = deviceConfigurationRepository;
        _productRepository = productRepository;
        _regionsRepository = regionsRepository;
        _areaRepository = areaRepository;
        _detectionTypeRepository = detectionTypeRepository;
        _measureTypeRepository = measureTypeRepository;
        _routeRepository = routeRepository;
        _routeLocationsRepository = routeLocationsRepository;
        _serviceProvider = serviceProvider;
        _deviceRepository = deviceRepository;
        _applicationConfiguration = applicationConfiguration ?? new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
    }

    public async Task RunAsync(TransferConfigCommandConfiguration options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _config = options;
        _cancellationToken = cancellationToken;
        _latestLocationIds = null;
        if (_config.Delete && string.IsNullOrWhiteSpace(_config.Source))
        {
            throw new InvalidOperationException("A source connection string is required before target configuration can be deleted.");
        }

        if (_config.Delete)
        {
            await SourcePreflightAsync(_config.Source, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        await EnsureTargetSchemaCompatibilityAsync(cancellationToken);
        if (_config.Delete)
        {
            await DeleteConfigurationDataAsync(cancellationToken);
        }

        // If no source connection string is provided (typical in unit tests),
        // avoid running import paths that open real SQL connections.
        if (string.IsNullOrWhiteSpace(_config.Source))
        {
            _logger.LogInformation("No source connection configured; skipping import/update steps.");
            _config.UpdateLocations = false;
            _config.ImportSpeedDevices = false;
        }

        Dictionary<string, string> queries = GetLocationQueries(_applicationConfiguration);
        var columnMappings = GetColumnMappings(_applicationConfiguration);
        if (_config.UpdateLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetDetectionTypeMesureType();
            cancellationToken.ThrowIfCancellationRequested();
            ImportProducts(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportDeviceConfigurations(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportRegions(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportAreas(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportJurisdictions(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportLocations(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportApproaches(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportDetectors(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportRoutes(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportRouteLocations(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ImportDevices(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ResetSequences();
        }
        if (_config.ImportSpeedDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportSpeedDevices(queries, columnMappings);
            cancellationToken.ThrowIfCancellationRequested();
            ResetSequences();
        }
    }

    private static async Task ValidateSourceAsync(string sourceConnectionString, CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(sourceConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.Signals', N'U') IS NOT NULL THEN 1 ELSE 0 END",
            connection)
        {
            CommandTimeout = 30
        };
        var hasSignalsTable = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        if (!hasSignalsTable)
        {
            throw new InvalidOperationException("The source database does not contain the required dbo.Signals table.");
        }
    }

    private void ResetSequences()
    {
        using var scope = _serviceProvider.CreateScope();
        var configContext = scope.ServiceProvider.GetRequiredService<ConfigContext>();

        // Check if the provider is PostgreSQL
        var databaseProvider = configContext.Database.ProviderName;
        if (databaseProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            _logger.LogInformation("Skipping sequence reset: database provider is not PostgreSQL");
            return;
        }

        string[] sequences = new string[]
        {
        "\"Locations_Id_seq\"",
        "\"Jurisdictions_Id_seq\"",
        "\"Approaches_Id_seq\"",
        "\"Detectors_Id_seq\"",
        "\"Products_Id_seq\"",
        "\"DeviceConfigurations_Id_seq\"",
        "\"Regions_Id_seq\"",
        "\"Devices_Id_seq\"",
        "\"Routes_Id_seq\"",
        "\"Areas_Id_seq\"",
        "\"MenuItems_Id_seq\"",
        "\"RouteLocations_Id_seq\""
        };

        foreach (string sequence in sequences)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            // Correctly format table name derived from sequence name
            string tableName = sequence.Replace("_Id_seq\"", "").Replace("\"", "");

            string query = $"SELECT setval('public.{sequence}',(SELECT COALESCE(MAX(\"Id\"), 0) FROM public.\"{tableName}\") + 1);";

            configContext.Database.ExecuteSqlRaw(query);

            _logger.LogInformation("Sequence {Sequence} reset successfully", sequence);
        }
    }


    private void ImportSpeedDevices(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_productRepository.GetList().Any(p => p.Manufacturer == "Wavetronix"))
        {
            _logger.LogInformation("Speed Product already exist");
        }
        else
        {
            _productRepository.Add(new Product { Manufacturer = "Wavetronix", Model = "Speed Detection" });
        }
        if (_deviceConfigurationRepository.GetList().Any(dc => dc.Description == "Speed"))
        {
            _logger.LogInformation("Speed Device Configuration already exist");
        }
        else
        {
            _deviceConfigurationRepository.Add(new DeviceConfiguration
            {
                Description = "Speed",
                Protocol = TransportProtocols.Unknown,
                ConnectionTimeout = 2000,
                Path = "Unknown",
                OperationTimeout = 2000,
                Port = 0,
                UserName = "Unknown",
                Password = "Unknown",
                ProductId = _productRepository.GetList().First(p => p.Manufacturer == "Wavetronix").Id
            });
        }
        _logger.LogInformation($"Importing Speed Devices");
        var importedDevices = ImportData<Device>(queries["SpeedDevices"], columnMappings["SpeedDevices"]);
        var speedDeviceConfiguration = _deviceConfigurationRepository.GetList().First(dc => dc.Description == "Speed");
        var latestLocationIds = GetLatestLocationIds();
        var validDevices = new List<Device>();
        foreach (var device in importedDevices)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            device.DeviceConfiguration = speedDeviceConfiguration;
            if (!latestLocationIds.TryGetValue(device.DeviceIdentifier, out var locationId))
            {
                _logger.LogInformation($"Location not found for device {device.DeviceIdentifier}");
                continue;
            }

            device.LocationId = locationId;
            validDevices.Add(device);
        }

        if (_config.Delete)
        {
            try
            {
                _deviceRepository.AddRange(validDevices);
                _logger.LogInformation($"Speed Devices Imported");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing speed devices");
                throw;
            }
        }
        else
        {
            var existingDevices = _deviceRepository.GetList()
                .Where(d => d.DeviceType == DeviceTypes.SpeedSensor)
                .AsEnumerable()
                .GroupBy(d => d.DeviceIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var newDevices = new List<Device>();

            foreach (var device in validDevices)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (existingDevices.TryGetValue(device.DeviceIdentifier, out var existingDevice))
                    {
                        existingDevice.DeviceConfigurationId = speedDeviceConfiguration.Id;
                        existingDevice.DeviceConfiguration = speedDeviceConfiguration;
                        existingDevice.LocationId = device.LocationId;
                        existingDevice.DeviceType = device.DeviceType;
                        existingDevice.Ipaddress = device.Ipaddress;
                        existingDevice.LoggingEnabled = device.LoggingEnabled;
                        existingDevice.DeviceStatus = device.DeviceStatus;
                        _deviceRepository.Update(existingDevice);
                        _logger.LogInformation($"Speed Device {device.DeviceIdentifier} Updated");
                    }
                    else
                    {
                        newDevices.Add(device);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing speed device {DeviceIdentifier}", device.DeviceIdentifier);
                }
            }

            if (newDevices.Count != 0)
            {
                _deviceRepository.AddRange(newDevices);
                _logger.LogInformation("Imported {Count} new speed devices", newDevices.Count);
            }
        }
    }

    private void DeleteProducts()
    {
        _logger.LogInformation($"Deleting all products");
        try
        {
            _productRepository.RemoveRange(_productRepository.GetList().ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting products");
            throw;
        }
    }

    private void DeleteDevicesConfigurations()
    {
        _logger.LogInformation($"Deleting all device configurations");
        try
        {
            _deviceConfigurationRepository.RemoveRange(_deviceConfigurationRepository.GetList().ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting device configurations");
            throw;
        }
    }

    private void ImportJurisdictions(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_jurisdictionRepository.GetList().Any())
        {
            _logger.LogInformation("Jurisdictions already exist");
            return;
        }
        _logger.LogInformation($"Importing Jurisdictions...");
        var jurisdictions = ImportData<Jurisdiction>(queries["Jurisdictions"], columnMappings["Jurisdictions"]);
        AddEntitiesWithIdentityInsert(jurisdictions, entities => _jurisdictionRepository.AddRange(entities));
        _logger.LogInformation($"Jurisdictions Imported");
    }

    private void ImportAreas(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_areaRepository.GetList().Any())
        {
            _logger.LogInformation("Areas already exist");
            return;
        }
        _logger.LogInformation($"Importing Areas...");
        var areas = ImportData<Area>(queries["Areas"], columnMappings["Areas"]);
        AddEntitiesWithIdentityInsert(areas, entities => _areaRepository.AddRange(entities));
        _logger.LogInformation($"Areas Imported");
    }

    private void ImportRegions(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_regionsRepository.GetList().Any())
        {
            _logger.LogInformation("Regions already exist");
            return;
        }
        _logger.LogInformation($"Importing Regions...");
        var regions = ImportData<Region>(queries["Regions"], columnMappings["Regions"]);
        AddEntitiesWithIdentityInsert(regions, entities => _regionsRepository.AddRange(entities));
        _logger.LogInformation($"Regions Imported");
    }

    private void ImportRoutes(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_routeRepository.GetList().Any())
        {
            _logger.LogInformation("Routes already exist");
            return;
        }
        _logger.LogInformation($"Importing Routes");
        var routes = ImportData<Route>(queries["Routes"], columnMappings["Routes"]);
        AddEntitiesWithIdentityInsert(routes, entities => _routeRepository.AddRange(entities));
        _logger.LogInformation($"Routes Imported");
    }

    private void ImportRouteLocations(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_routeLocationsRepository.GetList().Any())
        {
            _logger.LogInformation("Route Locations already exist");
            return;
        }
        _logger.LogInformation($"Importing Route Locations");
        var routeLocations = ImportData<RouteLocation>(queries["RouteLocations"], columnMappings["RouteLocations"]);
        AddEntitiesWithIdentityInsert(routeLocations, entities => _routeLocationsRepository.AddRange(entities));
        _logger.LogInformation($"Route Locations Imported");
    }


    private void ImportDeviceConfigurations(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_deviceConfigurationRepository.GetList().Any())
        {
            _logger.LogInformation("Device Configurations already exist");
            return;
        }
        _logger.LogInformation("Adding Device Configurations");
        var deviceConfigurations = ImportData<DeviceConfiguration>(queries["DeviceConfigurations"], columnMappings["DeviceConfigurations"]);
        AddEntitiesWithIdentityInsert(deviceConfigurations, entities => _deviceConfigurationRepository.AddRange(entities));
        _logger.LogInformation("Device Configurations Added");
    }

    private void ImportProducts(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_productRepository.GetList().Any())
        {
            _logger.LogInformation("Products already exist");
            return;
        }
        _logger.LogInformation("Adding Products");
        var products = ImportData<Product>(queries["Products"], columnMappings["Products"]);
        AddEntitiesWithIdentityInsert(products, entities => _productRepository.AddRange(entities));
        _logger.LogInformation("Products Added");
    }

    private void ImportApproaches(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_config.Delete && _approachRepository.GetList().Any())
        {
            _logger.LogInformation("Approaches already exist");
            return;
        }

        _logger.LogInformation($"Importing Approaches");

        // Import all approaches at once
        var approaches = ImportData<Approach>(queries["Approaches"], columnMappings["Approaches"]);

        if (_config.Delete == true)
        {
            const int batchSize = 5000;
            int total = approaches.Count;
            int batches = (int)Math.Ceiling(total / (double)batchSize);

            for (int i = 0; i < batches; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var batch = approaches.Skip(i * batchSize).Take(batchSize).ToList();
                AddEntitiesWithIdentityInsert(batch, entities => _approachRepository.AddRange(entities));
                _logger.LogInformation($"Batch {i + 1}/{batches} imported ({batch.Count} approaches).");
            }

            _logger.LogInformation($"All Approaches Imported");
        }
        else
        {
            var approachIds = _approachRepository.GetList().Select(a => a.Id).ToHashSet();
            var newApproaches = approaches.Where(a => !approachIds.Contains(a.Id)).ToList();
            if (newApproaches.Count != 0)
            {
                try
                {
                    AddEntitiesWithIdentityInsert(newApproaches, entities => _approachRepository.AddRange(entities));
                    _logger.LogInformation("Imported {Count} new approaches", newApproaches.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing {Count} new approaches", newApproaches.Count);
                    throw;
                }
            }
        }
    }

    private void ImportDevices(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        _logger.LogInformation($"Importing Devices");
        var importedDevices = ImportData<Device>(queries["Devices"], columnMappings["Devices"]);
        var configurations = _deviceConfigurationRepository.GetList().ToList();
        var latestLocationIds = GetLatestLocationIds();
        var validDevices = new List<Device>();
        foreach (var device in importedDevices)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var configuration = configurations.FirstOrDefault(c => c.Id == device.DeviceConfigurationId);
            if (configuration == null)
            {
                _logger.LogInformation($"Device Configuration not found for configuration {device.DeviceConfigurationId} on location{device.Notes}");
                continue;
            }

            if (!latestLocationIds.TryGetValue(device.DeviceIdentifier, out var locationId))
            {
                _logger.LogInformation($"Location not found for device {device.DeviceIdentifier}");
                continue;
            }

            device.DeviceConfiguration = configuration;
            device.LocationId = locationId;
            validDevices.Add(device);
        }

        if (_config.Delete)
        {
            _deviceRepository.AddRange(validDevices);
            _logger.LogInformation($"Devices Imported");
            return;
        }

        var existingDevices = _deviceRepository.GetList()
            .Where(d => d.DeviceType == DeviceTypes.SignalController)
            .AsEnumerable()
            .GroupBy(d => d.DeviceIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var newDevices = new List<Device>();

        foreach (var device in validDevices)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (existingDevices.TryGetValue(device.DeviceIdentifier, out var existingDevice))
            {
                existingDevice.DeviceConfigurationId = device.DeviceConfigurationId;
                existingDevice.DeviceConfiguration = device.DeviceConfiguration;
                existingDevice.LocationId = device.LocationId;
                existingDevice.Ipaddress = device.Ipaddress;
                existingDevice.LoggingEnabled = device.LoggingEnabled;
                existingDevice.DeviceStatus = device.DeviceStatus;
                existingDevice.Notes = device.Notes;
                existingDevice.DeviceType = device.DeviceType;
                _deviceRepository.Update(existingDevice);
                _logger.LogInformation($"Device {device.DeviceIdentifier} Updated");
            }
            else
            {
                newDevices.Add(device);
            }
        }

        if (newDevices.Count != 0)
        {
            _deviceRepository.AddRange(newDevices);
            _logger.LogInformation("Imported {Count} new controller devices", newDevices.Count);
        }
    }

    private void ImportDetectors(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_config.Delete == true && _detectorRepository.GetList().Any())
        {
            _logger.LogInformation("Detectors already exist");
            return;
        }
        _logger.LogInformation($"Importing Detectors");

        var detectionTypes = _detectionTypeRepository.GetList().ToList();

        var detectionTypeDetectors = ImportData<DetectionTypeDetector>(queries["DetectionTypeDetector"], columnMappings["DetectionTypeDetector"]);

        var detectors = ImportData<Detector>(queries["Detectors"], columnMappings["Detectors"]);


        if (_config.Delete)
        {
            const int batchSize = 5000;
            for (int i = 0; i < detectors.Count; i += batchSize)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var batch = detectors.Skip(i).Take(batchSize).ToList();
                WireDetectionTypesCore(batch, detectionTypes, detectionTypeDetectors, _cancellationToken);
                AddEntitiesWithIdentityInsert(batch, entities => _detectorRepository.AddRange(entities));
                _logger.LogInformation($"Processed batch of {batch.Count} detectors");
            }

            _logger.LogInformation($"Detectors Imported");
        }
        else
        {
            var detectorIds = _detectorRepository.GetList().Select(d => d.Id).ToHashSet();
            var newDetectors = detectors.Where(d => !detectorIds.Contains(d.Id)).ToList();
            if (newDetectors.Count != 0)
            {
                try
                {
                    WireDetectionTypesCore(newDetectors, detectionTypes, detectionTypeDetectors, _cancellationToken);
                    AddEntitiesWithIdentityInsert(newDetectors, entities => _detectorRepository.AddRange(entities));
                    _logger.LogInformation("Imported {Count} new detectors with detection-type mappings", newDetectors.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing {Count} new detectors", newDetectors.Count);
                    throw;
                }
            }
        }
    }

    private static void WireDetectionTypes(
        IReadOnlyCollection<Detector> detectors,
        IReadOnlyCollection<DetectionType> detectionTypes,
        IReadOnlyCollection<DetectionTypeDetector> detectionTypeDetectors)
    {
        WireDetectionTypesCore(detectors, detectionTypes, detectionTypeDetectors, CancellationToken.None);
    }

    private static void WireDetectionTypesCore(
        IReadOnlyCollection<Detector> detectors,
        IReadOnlyCollection<DetectionType> detectionTypes,
        IReadOnlyCollection<DetectionTypeDetector> detectionTypeDetectors,
        CancellationToken cancellationToken)
    {
        foreach (var detectionType in detectionTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detectorIds = detectionType.Id == DetectionTypes.B
                ? null
                : detectionTypeDetectors
                    .Where(mapping => mapping.DetectionTypesId == (int)detectionType.Id)
                    .Select(mapping => mapping.DetectorsId)
                    .ToHashSet();

            foreach (var detector in detectors.Where(detector => detectorIds == null || detectorIds.Contains(detector.Id)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                detectionType.Detectors.Add(detector);
            }
        }
    }

    private void ImportLocations(Dictionary<string, string> queries, Dictionary<string, Dictionary<string, string>> columnMappings)
    {
        if (_config.Delete == true && _locationRepository.GetList().Any())
        {
            _logger.LogInformation("Locations already exist");
            return;
        }
        _logger.LogInformation($"Importing Locations...");

        var areas = _areaRepository.GetList().ToList();

        var locationAreas = ImportData<AreaLocation>(queries["AreaLocations"], columnMappings["AreaLocations"]);

        var locations = ImportData<Location>(queries["Locations"], columnMappings["Locations"]);

        foreach (var area in areas)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var locationIds = locationAreas.Where(l => l.AreasId == area.Id).Select(l => l.LocationsId).ToHashSet();
            foreach (var location in locations.Where(l => locationIds.Contains(l.Id)))
            {
                location.Areas.Add(area);
            }
        }
        if (_config.Delete)
        {
            try
            {
                AddEntitiesWithIdentityInsert(locations, entities => _locationRepository.AddRange(entities));
                _logger.LogInformation($"Locations Imported");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing locations");
                throw;
            }
        }
        else
        {
            var locationIds = _locationRepository.GetList().Select(l => l.Id).ToHashSet();
            var newLocations = locations.Where(l => !locationIds.Contains(l.Id)).ToList();
            if (newLocations.Count != 0)
            {
                try
                {
                    AddEntitiesWithIdentityInsert(newLocations, entities => _locationRepository.AddRange(entities));
                    _logger.LogInformation("Imported {Count} new locations", newLocations.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing {Count} new locations", newLocations.Count);
                    throw;
                }
            }
        }
    }

    private void AddEntitiesWithIdentityInsert<TEntity>(List<TEntity> entities, Action<List<TEntity>> addRange)
        where TEntity : class
    {
        var configContext = _serviceProvider.GetRequiredService<ConfigContext>();

        if (!configContext.Database.IsSqlServer() || entities.Count == 0)
        {
            addRange(entities);
            return;
        }
        // When SqlServer is used, we need to enable IDENTITY_INSERT for the table to allow inserting values into identity columns.
        var entityType = configContext.Model.FindEntityType(typeof(TEntity))!;
        var qualifiedTableName = $"[{entityType.GetSchema() ?? "dbo"}].[{entityType.GetTableName()}]";
        var identityInsertOnSql = $"SET IDENTITY_INSERT {qualifiedTableName} ON;";
        var identityInsertOffSql = $"SET IDENTITY_INSERT {qualifiedTableName} OFF;";

        configContext.Database.OpenConnection();
        try
        {
            configContext.Database.ExecuteSqlRaw(identityInsertOnSql);
            addRange(entities);
        }
        finally
        {
            configContext.Database.ExecuteSqlRaw(identityInsertOffSql);
            configContext.Database.CloseConnection();
        }
    }

    private void SetDetectionTypeMesureType()
    {
        if (_detectionTypeRepository.GetList()
            .Include(d => d.MeasureTypes)
            .SelectMany(d => d.MeasureTypes)
            .Any())
        {
            _logger.LogInformation("Detection Types to Measure Types already exist");
            return;
        }
        var detectionTypes = _detectionTypeRepository.GetList().ToList();
        var measureTypes = _measureTypeRepository.GetList().ToList();
        var measureTypesForBasic = measureTypes.Where(m => new List<int> { 1, 2, 3, 4, 14, 15, 17, 31 }.Contains(m.Id)).ToList();
        var measureTypesForAdvanceCount = measureTypes.Where(m => new List<int> { 6, 7, 8, 9, 13, 32 }.Contains(m.Id)).ToList();
        var measureTypesForAdvanceSpeed = measureTypes.Where(m => new List<int> { 10 }.Contains(m.Id)).ToList();
        var measureTypesForLlc = measureTypes.Where(m => new List<int> { 5, 7, 31, 36 }.Contains(m.Id)).ToList();
        var measureTypesForLls = measureTypes.Where(m => new List<int> { 11 }.Contains(m.Id)).ToList();
        var measureTypesForStopBarPresence = measureTypes.Where(m => new List<int> { 12, 31, 32 }.Contains(m.Id)).ToList();
        foreach (var detectionType in detectionTypes)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            switch (detectionType.Id)
            {
                case DetectionTypes.B:
                    foreach (var measureType in measureTypesForBasic)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        detectionType.MeasureTypes.Add(measureType);
                    }
                    break;
                case DetectionTypes.AC:
                    foreach (var measureType in measureTypesForAdvanceCount)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        detectionType.MeasureTypes.Add(measureType);
                    }
                    break;
                case DetectionTypes.AS:
                    foreach (var measureType in measureTypesForAdvanceSpeed)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        detectionType.MeasureTypes.Add(measureType);
                    }
                    break;
                case DetectionTypes.LLC:
                    foreach (var measureType in measureTypesForLlc)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        detectionType.MeasureTypes.Add(measureType);
                    }
                    break;
                case DetectionTypes.LLS:
                    foreach (var measureType in measureTypesForLls)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        detectionType.MeasureTypes.Add(measureType);
                    }
                    break;
                case DetectionTypes.SBP:
                    foreach (var measureType in measureTypesForStopBarPresence)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        detectionType.MeasureTypes.Add(measureType);
                    }
                    break;
            }
            _detectionTypeRepository.Update(detectionType);
        }
    }

    private void DeleteJurisdictions()
    {
        _logger.LogInformation($"Deleting all jurisdictions");
        try
        {
            var jurisdictions = _jurisdictionRepository.GetList().ToList();
            _jurisdictionRepository.RemoveRange(jurisdictions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting jurisdictions");
            throw;
        }
    }

    private void DeleteAreas()
    {
        _logger.LogInformation($"Deleting all areas");
        try
        {
            var areas = _areaRepository.GetList().ToList();
            _areaRepository.RemoveRange(areas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting areas");
            throw;
        }
    }

    private void DeleteRegions()
    {
        _logger.LogInformation($"Deleting all regions");
        try
        {
            var regions = _regionsRepository.GetList().ToList();
            _regionsRepository.RemoveRange(regions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting regions");
            throw;
        }

    }

    private async Task DeleteConfigurationDataAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var configContext = scope.ServiceProvider.GetRequiredService<ConfigContext>();
        await using var transaction = configContext.Database.IsRelational()
            ? await configContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        _logger.LogInformation("Deleting the bounded target configuration set");
        await RemoveAllAsync<RouteLocation>(configContext, cancellationToken);
        await RemoveAllAsync<Route>(configContext, cancellationToken);
        await RemoveAllAsync<Device>(configContext, cancellationToken);
        await RemoveAllAsync<Detector>(configContext, cancellationToken);
        await RemoveAllAsync<Approach>(configContext, cancellationToken);
        await RemoveAllAsync<Location>(configContext, cancellationToken);
        await RemoveAllAsync<Area>(configContext, cancellationToken);
        await RemoveAllAsync<Jurisdiction>(configContext, cancellationToken);
        await RemoveAllAsync<Region>(configContext, cancellationToken);
        await RemoveAllAsync<DeviceConfiguration>(configContext, cancellationToken);
        await RemoveAllAsync<Product>(configContext, cancellationToken);
        await configContext.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task RemoveAllAsync<TEntity>(ConfigContext context, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entities = await context.Set<TEntity>().ToListAsync(cancellationToken);
        context.RemoveRange(entities);
    }

    private async Task EnsureTargetSchemaCompatibilityAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var configContext = scope.ServiceProvider.GetRequiredService<ConfigContext>();

        if (configContext.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        var pendingMigrations = (await configContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pendingMigrations.Count > 0)
        {
            _logger.LogInformation(
                "Applying {Count} pending PostgreSQL config migrations: {Migrations}",
                pendingMigrations.Count,
                string.Join(", ", pendingMigrations));
            await configContext.Database.MigrateAsync(cancellationToken);
        }

        await ApplyPostgreSqlConfig53MigrationBridgeAsync(configContext, cancellationToken);
    }

    internal const string PostgreSqlConfigCompatibilitySql = """
            DO $$
            DECLARE
                target_table text;
                audit_tables text[] := ARRAY[
                    'WatchDogIgnoreEvents',
                    'UsageEntries',
                    'Routes',
                    'RouteLocations',
                    'RouteDistances',
                    'Regions',
                    'Products',
                    'MenuItems',
                    'MeasureType',
                    'MeasureOptions',
                    'MeasureOptionPresets',
                    'MeasureComments',
                    'LocationTypes',
                    'Locations',
                    'Jurisdictions',
                    'Faqs',
                    'DirectionTypes',
                    'Devices',
                    'DeviceConfigurations',
                    'Detectors',
                    'DetectorComments',
                    'DetectionTypes',
                    'Areas',
                    'Approaches'
                ];
            BEGIN
                FOREACH target_table IN ARRAY audit_tables LOOP
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                            AND table_name = target_table
                            AND column_name = 'Created'
                            AND data_type = 'timestamp without time zone'
                    ) THEN
                        EXECUTE format(
                            'ALTER TABLE public.%I ALTER COLUMN "Created" TYPE timestamp with time zone USING "Created" AT TIME ZONE ''UTC''',
                            target_table);
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                            AND table_name = target_table
                            AND column_name = 'Modified'
                            AND data_type = 'timestamp without time zone'
                    ) THEN
                        EXECUTE format(
                            'ALTER TABLE public.%I ALTER COLUMN "Modified" TYPE timestamp with time zone USING "Modified" AT TIME ZONE ''UTC''',
                            target_table);
                    END IF;
                END LOOP;

                IF NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                        AND table_name = 'Approaches'
                        AND column_name = 'TransitSignalPriorityNumber'
                ) THEN
                    ALTER TABLE public."Approaches" ADD COLUMN "TransitSignalPriorityNumber" integer NULL;
                END IF;

            END $$;
            """;

    private async Task ApplyPostgreSqlConfig53MigrationBridgeAsync(ConfigContext configContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ensuring PostgreSQL config migration 20260521163837_5_3 compatibility");
        await configContext.Database.ExecuteSqlRawAsync(PostgreSqlConfigCompatibilitySql, cancellationToken);
    }

    private IReadOnlyDictionary<string, int> GetLatestLocationIds()
    {
        return _latestLocationIds ??= _locationRepository.GetList()
            .Where(location => location.VersionAction != LocationVersionActions.Delete)
            .AsEnumerable()
            .GroupBy(location => location.LocationIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(location => location.Start).ThenByDescending(location => location.Id).First().Id,
                StringComparer.OrdinalIgnoreCase);
    }


    private List<T> ImportData<T>(string query, Dictionary<string, string> columnMappings) where T : new()
    {
        var entities = new List<T>();
        if (string.IsNullOrWhiteSpace(_config?.Source))
        {
            _logger.LogWarning("ImportData skipped: source connection string is not set.");
            return entities;
        }

        using (var sourceConnection = new SqlConnection(_config.Source))
        {
            sourceConnection.OpenAsync(_cancellationToken).GetAwaiter().GetResult();
            using (SqlCommand sourceCommand = CreateSourceCommand(query, sourceConnection))
            {
                using (SqlDataReader reader = sourceCommand.ExecuteReaderAsync(_cancellationToken).GetAwaiter().GetResult())
                {
                    while (reader.Read())
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        var entity = new T(); // Create an instance of the generic type

                        // Iterate through column mappings

                        foreach (var mapping in columnMappings)
                        {
                            var propertyName = mapping.Value; // Name of the property in the class
                            var columnName = mapping.Key;       // Name of the column in the data reader

                            // Get the value from the reader
                            var value = reader[columnName];

                            // Use reflection to set the property value
                            var propertyInfo = entity.GetType().GetProperty(propertyName);
                            if (propertyInfo != null && value != DBNull.Value)
                            {
                                var propertyType = propertyInfo.PropertyType;
                                // Handle nullable types
                                var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                                // Special handling for enums
                                if (targetType.IsEnum)
                                {
                                    var enumValue = Enum.ToObject(targetType, value);
                                    propertyInfo.SetValue(entity, enumValue);
                                }
                                else if (targetType == typeof(Dictionary<string, object>))
                                {
                                    // Convert the value to string (which should be valid JSON) and deserialize it
                                    var jsonString = Convert.ChangeType(value, typeof(string)) as string;
                                    if (!string.IsNullOrWhiteSpace(jsonString))
                                    {
                                        try
                                        {
                                            var dictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                                            propertyInfo.SetValue(entity, dictionary);
                                        }
                                        catch (JsonException ex)
                                        {
                                            throw new InvalidOperationException(
                                                $"Failed to deserialize JSON for property '{propertyName}'.", ex);
                                        }
                                    }
                                }
                                else if (targetType == typeof(string[]))
                                {
                                    // Convert the value to string and then deserialize the JSON array
                                    var jsonString = Convert.ChangeType(value, typeof(string)) as string;
                                    if (!string.IsNullOrWhiteSpace(jsonString))
                                    {
                                        try
                                        {
                                            var arrayValue = JsonSerializer.Deserialize<string[]>(jsonString) ?? Array.Empty<string>();
                                            propertyInfo.SetValue(entity, arrayValue);
                                        }
                                        catch (JsonException ex)
                                        {
                                            throw new InvalidOperationException(
                                                $"Failed to deserialize JSON array for property '{propertyName}'.", ex);
                                        }
                                    }
                                }
                                else if (targetType == typeof(string))
                                {
                                    // For string, simply convert it (optionally, unescape if needed)
                                    var stringValue = Convert.ChangeType(value, typeof(string)) as string;
                                    propertyInfo.SetValue(entity, stringValue);
                                }
                                else
                                {
                                    // Convert and assign other types
                                    propertyInfo.SetValue(entity, Convert.ChangeType(value, targetType));
                                }
                            }
                        }


                        entities.Add(entity);
                    }
                }
            }
        }

        return entities;
    }

    internal static SqlCommand CreateSourceCommand(string query, SqlConnection sourceConnection) =>
        new(query, sourceConnection)
        {
            CommandTimeout = SourceQueryTimeoutSeconds
        };

    public static Dictionary<string, Dictionary<string, string>> GetColumnMappings(IConfiguration configuration)
    {
        // Retrieve the "ColumnMappings" section from the configuration
        var columnMappingsSection = configuration.GetSection("ColumnMappings");

        if (!columnMappingsSection.Exists())
        {
            throw new KeyNotFoundException("The 'ColumnMappings' section was not found in the configuration.");
        }

        // Create the dictionary to hold the mappings
        var columnMappings = new Dictionary<string, Dictionary<string, string>>();

        foreach (var tableSection in columnMappingsSection.GetChildren())
        {
            var tableName = tableSection.Key;
            var columnMap = new Dictionary<string, string>();

            foreach (var columnSection in tableSection.GetChildren())
            {
                columnMap[columnSection.Key] = columnSection.Value ?? string.Empty;
            }

            columnMappings[tableName] = columnMap;
        }

        return columnMappings;
    }

    private Dictionary<string, string> GetLocationQueries(IConfiguration config)
    {
        var queries = new Dictionary<string, string>();

        // Get the "LocationQueries" section from the config
        var locationQueriesSection = config.GetSection("LocationQueries");

        if (!locationQueriesSection.Exists())
            throw new InvalidOperationException("The 'LocationQueries' section is missing in the configuration.");

        // Iterate through each key-value pair in the section
        foreach (var child in locationQueriesSection.GetChildren())
        {
            queries[child.Key] = child.Value ?? string.Empty;
        }

        return queries;
    }

    //private void ManuallyAddSpeedDevice()
    //{
    //    productRepository.Add(new Product { Id = 11, Manufacturer = "Wavetronix", Model = "Wavetronix Advance Detection" });
    //    deviceConfigurationRepository.Add(new Device
    //    {
    //        Id = 11,
    //        Description = "None",
    //        Protocol = ATSPM.Table.Enums.TransportProtocols.Unknown,
    //        ConnectionTimeout = 2000,
    //        Directory = "Unknown",
    //        OperationTimout = 2000,
    //        Port = 0,
    //        UserName = "Unknown",
    //        Password = "Unknown",
    //        ProductId = 11
    //    });
    //    deviceRepository.Add(new Device
    //    {
    //         DeviceConfigurationId= 11, DeviceStatus = ATSPM.Table.Enums.DeviceStatus.Active, Ipaddress = "127.0.0.1", 
    //    });
    //}



    private void DeleteLocations()
    {
        _logger.LogInformation($"Deleting all locations");
        try
        {
            var locations = _locationRepository.GetList().ToList();
            _locationRepository.RemoveRange(locations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting locations");
            throw;
        }
    }

    private void DeleteDevices()
    {
        _logger.LogInformation($"Deleting all devices");
        try
        {
            _deviceRepository.RemoveRange(_deviceRepository.GetList().ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting devices");
            throw;
        }
    }



}


public class AreaLocation
{
    public int AreasId { get; set; }
    public int LocationsId { get; set; }
}

public class DetectionTypeDetector
{
    public int DetectionTypesId { get; set; }
    public int DetectorsId { get; set; }
}





