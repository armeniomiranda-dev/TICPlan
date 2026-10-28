using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <summary>
    /// Schema "gtfs" (PostGIS): uma tabela por ficheiro da referência GTFS Schedule. Cada linha pertence a uma versão
    /// do feed (feed_id → public.gtfs_feeds, ON DELETE CASCADE) e as chaves GTFS (stop_id, trip_id, …) são únicas
    /// dentro do feed, por isso as chaves primárias e estrangeiras são compostas (feed_id, …). As FKs internas são
    /// DEFERRABLE INITIALLY DEFERRED para permitir carregar os ficheiros por qualquer ordem numa transação.
    /// Horas GTFS (podem passar das 24:00) em interval; datas em date; enumerações em smallint com CHECK.
    /// </summary>
    public partial class GtfsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS postgis;
                CREATE SCHEMA IF NOT EXISTS gtfs;
                COMMENT ON SCHEMA gtfs IS 'Conteúdo dos feeds GTFS: uma tabela por ficheiro; feed_id = versão do feed (public.gtfs_feeds).';

                -- feed_info.txt (uma linha por feed)
                CREATE TABLE gtfs.feed_info (
                    feed_id             integer PRIMARY KEY REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    feed_publisher_name text NOT NULL,
                    feed_publisher_url  text NOT NULL,
                    feed_lang           text NOT NULL,
                    default_lang        text,
                    feed_start_date     date,
                    feed_end_date       date,
                    feed_version        text,
                    feed_contact_email  text,
                    feed_contact_url    text,
                    CHECK (feed_end_date IS NULL OR feed_start_date IS NULL OR feed_end_date >= feed_start_date)
                );

                -- agency.txt ('' quando o feed tem uma só agência sem agency_id)
                CREATE TABLE gtfs.agency (
                    feed_id         integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    agency_id       text NOT NULL DEFAULT '',
                    agency_name     text NOT NULL,
                    agency_url      text NOT NULL,
                    agency_timezone text NOT NULL,
                    agency_lang     text,
                    agency_phone    text,
                    agency_fare_url text,
                    agency_email    text,
                    cemv_support    smallint CHECK (cemv_support BETWEEN 0 AND 2),
                    PRIMARY KEY (feed_id, agency_id)
                );

                -- levels.txt
                CREATE TABLE gtfs.levels (
                    feed_id     integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    level_id    text NOT NULL,
                    level_index double precision NOT NULL,
                    level_name  text,
                    PRIMARY KEY (feed_id, level_id)
                );

                -- stops.txt (geom calculado a partir de stop_lat/stop_lon)
                CREATE TABLE gtfs.stops (
                    feed_id             integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    stop_id             text NOT NULL,
                    stop_code           text,
                    stop_name           text,
                    tts_stop_name       text,
                    stop_desc           text,
                    stop_lat            double precision CHECK (stop_lat BETWEEN -90 AND 90),
                    stop_lon            double precision CHECK (stop_lon BETWEEN -180 AND 180),
                    zone_id             text,
                    stop_url            text,
                    location_type       smallint CHECK (location_type BETWEEN 0 AND 4),
                    parent_station      text,
                    stop_timezone       text,
                    wheelchair_boarding smallint CHECK (wheelchair_boarding BETWEEN 0 AND 2),
                    level_id            text,
                    platform_code       text,
                    stop_access         smallint CHECK (stop_access BETWEEN 0 AND 1),
                    geom                geometry(Point, 4326) GENERATED ALWAYS AS (
                                            CASE WHEN stop_lat IS NOT NULL AND stop_lon IS NOT NULL
                                                 THEN ST_SetSRID(ST_MakePoint(stop_lon, stop_lat), 4326) END) STORED,
                    PRIMARY KEY (feed_id, stop_id),
                    FOREIGN KEY (feed_id, parent_station) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, level_id) REFERENCES gtfs.levels (feed_id, level_id) DEFERRABLE INITIALLY DEFERRED
                );
                CREATE INDEX ix_gtfs_stops_geom ON gtfs.stops USING gist (geom);
                CREATE INDEX ix_gtfs_stops_parent_station ON gtfs.stops (feed_id, parent_station);

                -- networks.txt
                CREATE TABLE gtfs.networks (
                    feed_id      integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    network_id   text NOT NULL,
                    network_name text,
                    PRIMARY KEY (feed_id, network_id)
                );

                -- routes.txt
                CREATE TABLE gtfs.routes (
                    feed_id             integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    route_id            text NOT NULL,
                    agency_id           text,
                    route_short_name    text,
                    route_long_name     text,
                    route_desc          text,
                    route_type          integer NOT NULL CHECK (route_type >= 0),
                    route_url           text,
                    route_color         text CHECK (route_color ~ '^[0-9A-Fa-f]{6}$'),
                    route_text_color    text CHECK (route_text_color ~ '^[0-9A-Fa-f]{6}$'),
                    route_sort_order    integer CHECK (route_sort_order >= 0),
                    continuous_pickup   smallint CHECK (continuous_pickup BETWEEN 0 AND 3),
                    continuous_drop_off smallint CHECK (continuous_drop_off BETWEEN 0 AND 3),
                    network_id          text,
                    cemv_support        smallint CHECK (cemv_support BETWEEN 0 AND 2),
                    PRIMARY KEY (feed_id, route_id),
                    FOREIGN KEY (feed_id, agency_id) REFERENCES gtfs.agency (feed_id, agency_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, network_id) REFERENCES gtfs.networks (feed_id, network_id) DEFERRABLE INITIALLY DEFERRED
                );

                -- route_networks.txt
                CREATE TABLE gtfs.route_networks (
                    feed_id    integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    network_id text NOT NULL,
                    route_id   text NOT NULL,
                    PRIMARY KEY (feed_id, route_id),
                    FOREIGN KEY (feed_id, network_id) REFERENCES gtfs.networks (feed_id, network_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, route_id) REFERENCES gtfs.routes (feed_id, route_id) DEFERRABLE INITIALLY DEFERRED
                );

                -- calendar.txt
                CREATE TABLE gtfs.calendar (
                    feed_id    integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    service_id text NOT NULL,
                    monday     boolean NOT NULL,
                    tuesday    boolean NOT NULL,
                    wednesday  boolean NOT NULL,
                    thursday   boolean NOT NULL,
                    friday     boolean NOT NULL,
                    saturday   boolean NOT NULL,
                    sunday     boolean NOT NULL,
                    start_date date NOT NULL,
                    end_date   date NOT NULL,
                    PRIMARY KEY (feed_id, service_id),
                    CHECK (end_date >= start_date)
                );

                -- calendar_dates.txt (sem FK para calendar: um service_id pode existir só aqui)
                CREATE TABLE gtfs.calendar_dates (
                    feed_id        integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    service_id     text NOT NULL,
                    date           date NOT NULL,
                    exception_type smallint NOT NULL CHECK (exception_type IN (1, 2)),
                    PRIMARY KEY (feed_id, service_id, date)
                );

                -- shapes.txt (pontos) e shape_geoms (uma linha PostGIS por shape_id, ver gtfs.gerar_shape_geoms)
                CREATE TABLE gtfs.shapes (
                    feed_id             integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    shape_id            text NOT NULL,
                    shape_pt_lat        double precision NOT NULL CHECK (shape_pt_lat BETWEEN -90 AND 90),
                    shape_pt_lon        double precision NOT NULL CHECK (shape_pt_lon BETWEEN -180 AND 180),
                    shape_pt_sequence   integer NOT NULL CHECK (shape_pt_sequence >= 0),
                    shape_dist_traveled double precision CHECK (shape_dist_traveled >= 0),
                    PRIMARY KEY (feed_id, shape_id, shape_pt_sequence)
                );

                CREATE TABLE gtfs.shape_geoms (
                    feed_id    integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    shape_id   text NOT NULL,
                    num_pontos integer NOT NULL,
                    geom       geometry(LineString, 4326) NOT NULL,
                    PRIMARY KEY (feed_id, shape_id)
                );
                CREATE INDEX ix_gtfs_shape_geoms_geom ON gtfs.shape_geoms USING gist (geom);

                -- trips.txt (service_id sem FK: pode vir de calendar ou de calendar_dates; shape_id → shape_geoms)
                CREATE TABLE gtfs.trips (
                    feed_id               integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    route_id              text NOT NULL,
                    service_id            text NOT NULL,
                    trip_id               text NOT NULL,
                    trip_headsign         text,
                    trip_short_name       text,
                    direction_id          smallint CHECK (direction_id IN (0, 1)),
                    block_id              text,
                    shape_id              text,
                    wheelchair_accessible smallint CHECK (wheelchair_accessible BETWEEN 0 AND 2),
                    bikes_allowed         smallint CHECK (bikes_allowed BETWEEN 0 AND 2),
                    cars_allowed          smallint CHECK (cars_allowed BETWEEN 0 AND 2),
                    PRIMARY KEY (feed_id, trip_id),
                    FOREIGN KEY (feed_id, route_id) REFERENCES gtfs.routes (feed_id, route_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, shape_id) REFERENCES gtfs.shape_geoms (feed_id, shape_id) DEFERRABLE INITIALLY DEFERRED
                );
                CREATE INDEX ix_gtfs_trips_route ON gtfs.trips (feed_id, route_id);
                CREATE INDEX ix_gtfs_trips_service ON gtfs.trips (feed_id, service_id);

                -- locations.geojson (GTFS-Flex)
                CREATE TABLE gtfs.locations (
                    feed_id     integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    location_id text NOT NULL,
                    stop_name   text,
                    stop_desc   text,
                    properties  jsonb,
                    geom        geometry(Geometry, 4326) NOT NULL,
                    PRIMARY KEY (feed_id, location_id)
                );
                CREATE INDEX ix_gtfs_locations_geom ON gtfs.locations USING gist (geom);

                -- location_groups.txt / location_group_stops.txt
                CREATE TABLE gtfs.location_groups (
                    feed_id             integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    location_group_id   text NOT NULL,
                    location_group_name text,
                    PRIMARY KEY (feed_id, location_group_id)
                );

                CREATE TABLE gtfs.location_group_stops (
                    feed_id           integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    location_group_id text NOT NULL,
                    stop_id           text NOT NULL,
                    PRIMARY KEY (feed_id, location_group_id, stop_id),
                    FOREIGN KEY (feed_id, location_group_id) REFERENCES gtfs.location_groups (feed_id, location_group_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED
                );

                -- booking_rules.txt
                CREATE TABLE gtfs.booking_rules (
                    feed_id                   integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    booking_rule_id           text NOT NULL,
                    booking_type              smallint NOT NULL CHECK (booking_type BETWEEN 0 AND 2),
                    prior_notice_duration_min integer,
                    prior_notice_duration_max integer,
                    prior_notice_last_day     integer,
                    prior_notice_last_time    interval,
                    prior_notice_start_day    integer,
                    prior_notice_start_time   interval,
                    prior_notice_service_id   text,
                    message                   text,
                    pickup_message            text,
                    drop_off_message          text,
                    phone_number              text,
                    info_url                  text,
                    booking_url               text,
                    PRIMARY KEY (feed_id, booking_rule_id)
                );

                -- stop_times.txt
                CREATE TABLE gtfs.stop_times (
                    feed_id                      integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    trip_id                      text NOT NULL,
                    arrival_time                 interval,
                    departure_time               interval,
                    stop_id                      text,
                    location_group_id            text,
                    location_id                  text,
                    stop_sequence                integer NOT NULL CHECK (stop_sequence >= 0),
                    stop_headsign                text,
                    start_pickup_drop_off_window interval,
                    end_pickup_drop_off_window   interval,
                    pickup_type                  smallint CHECK (pickup_type BETWEEN 0 AND 3),
                    drop_off_type                smallint CHECK (drop_off_type BETWEEN 0 AND 3),
                    continuous_pickup            smallint CHECK (continuous_pickup BETWEEN 0 AND 3),
                    continuous_drop_off          smallint CHECK (continuous_drop_off BETWEEN 0 AND 3),
                    shape_dist_traveled          double precision CHECK (shape_dist_traveled >= 0),
                    timepoint                    smallint CHECK (timepoint IN (0, 1)),
                    pickup_booking_rule_id       text,
                    drop_off_booking_rule_id     text,
                    PRIMARY KEY (feed_id, trip_id, stop_sequence),
                    CHECK (num_nonnulls(stop_id, location_group_id, location_id) = 1),
                    FOREIGN KEY (feed_id, trip_id) REFERENCES gtfs.trips (feed_id, trip_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, location_group_id) REFERENCES gtfs.location_groups (feed_id, location_group_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, location_id) REFERENCES gtfs.locations (feed_id, location_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, pickup_booking_rule_id) REFERENCES gtfs.booking_rules (feed_id, booking_rule_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, drop_off_booking_rule_id) REFERENCES gtfs.booking_rules (feed_id, booking_rule_id) DEFERRABLE INITIALLY DEFERRED
                );
                CREATE INDEX ix_gtfs_stop_times_stop ON gtfs.stop_times (feed_id, stop_id);

                -- frequencies.txt
                CREATE TABLE gtfs.frequencies (
                    feed_id      integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    trip_id      text NOT NULL,
                    start_time   interval NOT NULL,
                    end_time     interval NOT NULL,
                    headway_secs integer NOT NULL CHECK (headway_secs > 0),
                    exact_times  smallint CHECK (exact_times IN (0, 1)),
                    PRIMARY KEY (feed_id, trip_id, start_time),
                    FOREIGN KEY (feed_id, trip_id) REFERENCES gtfs.trips (feed_id, trip_id) DEFERRABLE INITIALLY DEFERRED
                );

                -- transfers.txt
                CREATE TABLE gtfs.transfers (
                    id                bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id           integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    from_stop_id      text,
                    to_stop_id        text,
                    from_route_id     text,
                    to_route_id       text,
                    from_trip_id      text,
                    to_trip_id        text,
                    transfer_type     smallint NOT NULL DEFAULT 0 CHECK (transfer_type BETWEEN 0 AND 5),
                    min_transfer_time integer CHECK (min_transfer_time >= 0),
                    FOREIGN KEY (feed_id, from_stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, to_stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, from_route_id) REFERENCES gtfs.routes (feed_id, route_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, to_route_id) REFERENCES gtfs.routes (feed_id, route_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, from_trip_id) REFERENCES gtfs.trips (feed_id, trip_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, to_trip_id) REFERENCES gtfs.trips (feed_id, trip_id) DEFERRABLE INITIALLY DEFERRED
                );
                CREATE INDEX ix_gtfs_transfers_feed ON gtfs.transfers (feed_id);

                -- pathways.txt
                CREATE TABLE gtfs.pathways (
                    feed_id                integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    pathway_id             text NOT NULL,
                    from_stop_id           text NOT NULL,
                    to_stop_id             text NOT NULL,
                    pathway_mode           smallint NOT NULL CHECK (pathway_mode BETWEEN 1 AND 7),
                    is_bidirectional       smallint NOT NULL CHECK (is_bidirectional IN (0, 1)),
                    length                 double precision CHECK (length >= 0),
                    traversal_time         integer CHECK (traversal_time > 0),
                    stair_count            integer,
                    max_slope              double precision,
                    min_width              double precision CHECK (min_width > 0),
                    signposted_as          text,
                    reversed_signposted_as text,
                    PRIMARY KEY (feed_id, pathway_id),
                    FOREIGN KEY (feed_id, from_stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, to_stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED
                );

                -- fare_attributes.txt / fare_rules.txt (Fares v1)
                CREATE TABLE gtfs.fare_attributes (
                    feed_id           integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    fare_id           text NOT NULL,
                    price             numeric(14, 4) NOT NULL CHECK (price >= 0),
                    currency_type     char(3) NOT NULL,
                    payment_method    smallint NOT NULL CHECK (payment_method IN (0, 1)),
                    transfers         smallint CHECK (transfers BETWEEN 0 AND 2),
                    agency_id         text,
                    transfer_duration integer CHECK (transfer_duration >= 0),
                    PRIMARY KEY (feed_id, fare_id),
                    FOREIGN KEY (feed_id, agency_id) REFERENCES gtfs.agency (feed_id, agency_id) DEFERRABLE INITIALLY DEFERRED
                );

                CREATE TABLE gtfs.fare_rules (
                    id             bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id        integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    fare_id        text NOT NULL,
                    route_id       text,
                    origin_id      text,
                    destination_id text,
                    contains_id    text,
                    FOREIGN KEY (feed_id, fare_id) REFERENCES gtfs.fare_attributes (feed_id, fare_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, route_id) REFERENCES gtfs.routes (feed_id, route_id) DEFERRABLE INITIALLY DEFERRED
                );
                CREATE INDEX ix_gtfs_fare_rules_fare ON gtfs.fare_rules (feed_id, fare_id);

                -- Fares v2: timeframes, rider_categories, fare_media, fare_products, areas, stop_areas, fare_leg_rules,
                -- fare_leg_join_rules, fare_transfer_rules
                CREATE TABLE gtfs.timeframes (
                    id                 bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id            integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    timeframe_group_id text NOT NULL,
                    start_time         interval,
                    end_time           interval,
                    service_id         text NOT NULL
                );
                CREATE INDEX ix_gtfs_timeframes_group ON gtfs.timeframes (feed_id, timeframe_group_id);

                CREATE TABLE gtfs.rider_categories (
                    feed_id                  integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    rider_category_id        text NOT NULL,
                    rider_category_name      text NOT NULL,
                    is_default_fare_category smallint CHECK (is_default_fare_category IN (0, 1)),
                    eligibility_url          text,
                    PRIMARY KEY (feed_id, rider_category_id)
                );

                CREATE TABLE gtfs.fare_media (
                    feed_id         integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    fare_media_id   text NOT NULL,
                    fare_media_name text,
                    fare_media_type smallint NOT NULL CHECK (fare_media_type BETWEEN 0 AND 4),
                    PRIMARY KEY (feed_id, fare_media_id)
                );

                CREATE TABLE gtfs.fare_products (
                    id                bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id           integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    fare_product_id   text NOT NULL,
                    fare_product_name text,
                    rider_category_id text,
                    fare_media_id     text,
                    amount            numeric(14, 4) NOT NULL,
                    currency          char(3) NOT NULL,
                    UNIQUE NULLS NOT DISTINCT (feed_id, fare_product_id, rider_category_id, fare_media_id),
                    FOREIGN KEY (feed_id, rider_category_id) REFERENCES gtfs.rider_categories (feed_id, rider_category_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, fare_media_id) REFERENCES gtfs.fare_media (feed_id, fare_media_id) DEFERRABLE INITIALLY DEFERRED
                );

                CREATE TABLE gtfs.areas (
                    feed_id   integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    area_id   text NOT NULL,
                    area_name text,
                    PRIMARY KEY (feed_id, area_id)
                );

                CREATE TABLE gtfs.stop_areas (
                    feed_id integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    area_id text NOT NULL,
                    stop_id text NOT NULL,
                    PRIMARY KEY (feed_id, area_id, stop_id),
                    FOREIGN KEY (feed_id, area_id) REFERENCES gtfs.areas (feed_id, area_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED
                );

                CREATE TABLE gtfs.fare_leg_rules (
                    id                      bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id                 integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    leg_group_id            text,
                    network_id              text,
                    from_area_id            text,
                    to_area_id              text,
                    from_timeframe_group_id text,
                    to_timeframe_group_id   text,
                    fare_product_id         text NOT NULL,
                    rule_priority           integer CHECK (rule_priority >= 0),
                    FOREIGN KEY (feed_id, network_id) REFERENCES gtfs.networks (feed_id, network_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, from_area_id) REFERENCES gtfs.areas (feed_id, area_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, to_area_id) REFERENCES gtfs.areas (feed_id, area_id) DEFERRABLE INITIALLY DEFERRED
                );
                CREATE INDEX ix_gtfs_fare_leg_rules_feed ON gtfs.fare_leg_rules (feed_id);

                CREATE TABLE gtfs.fare_leg_join_rules (
                    id              bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id         integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    from_network_id text NOT NULL,
                    to_network_id   text NOT NULL,
                    from_stop_id    text,
                    to_stop_id      text,
                    FOREIGN KEY (feed_id, from_network_id) REFERENCES gtfs.networks (feed_id, network_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, to_network_id) REFERENCES gtfs.networks (feed_id, network_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, from_stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, to_stop_id) REFERENCES gtfs.stops (feed_id, stop_id) DEFERRABLE INITIALLY DEFERRED
                );

                CREATE TABLE gtfs.fare_transfer_rules (
                    id                  bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id             integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    from_leg_group_id   text,
                    to_leg_group_id     text,
                    transfer_count      integer CHECK (transfer_count = -1 OR transfer_count >= 1),
                    duration_limit      integer CHECK (duration_limit > 0),
                    duration_limit_type smallint CHECK (duration_limit_type BETWEEN 0 AND 3),
                    fare_transfer_type  smallint NOT NULL CHECK (fare_transfer_type BETWEEN 0 AND 2),
                    fare_product_id     text
                );
                CREATE INDEX ix_gtfs_fare_transfer_rules_feed ON gtfs.fare_transfer_rules (feed_id);

                -- translations.txt (record_id refere-se à tabela indicada em table_name; sem FK)
                CREATE TABLE gtfs.translations (
                    id            bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id       integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    table_name    text NOT NULL CHECK (table_name IN ('agency', 'stops', 'routes', 'trips', 'stop_times', 'pathways', 'levels', 'feed_info', 'attributions')),
                    field_name    text NOT NULL,
                    language      text NOT NULL,
                    translation   text NOT NULL,
                    record_id     text,
                    record_sub_id text,
                    field_value   text
                );
                CREATE INDEX ix_gtfs_translations_feed ON gtfs.translations (feed_id, table_name);

                -- attributions.txt
                CREATE TABLE gtfs.attributions (
                    id                bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    feed_id           integer NOT NULL REFERENCES public.gtfs_feeds (id) ON DELETE CASCADE,
                    attribution_id    text,
                    agency_id         text,
                    route_id          text,
                    trip_id           text,
                    organization_name text NOT NULL,
                    is_producer       smallint CHECK (is_producer IN (0, 1)),
                    is_operator       smallint CHECK (is_operator IN (0, 1)),
                    is_authority      smallint CHECK (is_authority IN (0, 1)),
                    attribution_url   text,
                    attribution_email text,
                    attribution_phone text,
                    FOREIGN KEY (feed_id, agency_id) REFERENCES gtfs.agency (feed_id, agency_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, route_id) REFERENCES gtfs.routes (feed_id, route_id) DEFERRABLE INITIALLY DEFERRED,
                    FOREIGN KEY (feed_id, trip_id) REFERENCES gtfs.trips (feed_id, trip_id) DEFERRABLE INITIALLY DEFERRED
                );

                -- Gera/atualiza gtfs.shape_geoms (LineString por shape_id) a partir dos pontos de gtfs.shapes.
                CREATE OR REPLACE FUNCTION gtfs.gerar_shape_geoms(p_feed_id integer) RETURNS integer
                LANGUAGE plpgsql AS $fn$
                DECLARE n integer;
                BEGIN
                    INSERT INTO gtfs.shape_geoms (feed_id, shape_id, num_pontos, geom)
                    SELECT feed_id, shape_id, count(*),
                           ST_SetSRID(ST_MakeLine(ST_MakePoint(shape_pt_lon, shape_pt_lat) ORDER BY shape_pt_sequence), 4326)
                    FROM gtfs.shapes
                    WHERE feed_id = p_feed_id
                    GROUP BY feed_id, shape_id
                    HAVING count(*) >= 2
                    ON CONFLICT (feed_id, shape_id) DO UPDATE SET num_pontos = EXCLUDED.num_pontos, geom = EXCLUDED.geom;
                    GET DIAGNOSTICS n = ROW_COUNT;
                    RETURN n;
                END
                $fn$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A extensão postgis fica (pode ser usada por outros schemas).
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS gtfs CASCADE;");
        }
    }
}
