-- DDL guard for the migrate phase: plan.sh puts this at the top of every migrate plan. Two temporary
-- helpers — they live in pg_temp, vanish with the session and never become part of the tenant schema:
--   pg_temp.thor_schema_fp()            one md5 over every object in the non-system schemas
--   pg_temp.thor_assert_schema(before)  raises if the schema no longer matches that md5
-- The plan takes the md5 before the backfill scripts and asserts after each one. DDL is transactional
-- and a transaction sees its own catalog changes, so a script that changed the schema — even through
-- EXECUTE in a DO block — fails the assert and the whole tenant rolls back. Data changes (rows,
-- sequence values, statistics) are not part of the md5. Migrate's own record table is left out.

CREATE FUNCTION pg_temp.thor_schema_fp() RETURNS text LANGUAGE sql AS $fp$
  WITH ns AS (
    SELECT oid, nspname, nspacl FROM pg_namespace
    WHERE nspname NOT LIKE 'pg\_%' AND nspname <> 'information_schema'
  ), rel AS (
    SELECT c.oid, ns.nspname || '.' || c.relname AS name, c.relkind, c.relacl
    FROM pg_class c JOIN ns ON ns.oid = c.relnamespace
    WHERE c.relname NOT LIKE '\_\_thor\_backfills%'
  )
  SELECT md5(coalesce(string_agg(x, E'\n' ORDER BY x), '')) FROM (
    SELECT 'schema ' || nspname || ' ' || coalesce(nspacl::text, '') FROM ns
    UNION ALL
    SELECT 'rel ' || name || ' ' || relkind::text || ' ' || coalesce(relacl::text, '') FROM rel
    UNION ALL
    SELECT 'col ' || r.name || '.' || a.attname || ' ' || format_type(a.atttypid, a.atttypmod) || ' '
           || a.attnotnull || ' ' || coalesce(pg_get_expr(d.adbin, d.adrelid), '') || ' '
           || coalesce(a.attacl::text, '')
    FROM rel r JOIN pg_attribute a ON a.attrelid = r.oid
    LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
    WHERE a.attnum > 0 AND NOT a.attisdropped
    UNION ALL
    SELECT 'con ' || r.name || ' ' || co.conname || ' ' || pg_get_constraintdef(co.oid)
    FROM pg_constraint co JOIN rel r ON r.oid = co.conrelid
    UNION ALL
    SELECT 'idx ' || pg_get_indexdef(i.indexrelid) FROM pg_index i JOIN rel r ON r.oid = i.indrelid
    UNION ALL
    SELECT 'view ' || r.name || ' ' || pg_get_viewdef(r.oid) FROM rel r WHERE r.relkind IN ('v', 'm')
    UNION ALL
    SELECT 'trg ' || r.name || ' ' || t.tgname || ' ' || t.tgenabled::text || ' ' || pg_get_triggerdef(t.oid)
    FROM pg_trigger t JOIN rel r ON r.oid = t.tgrelid WHERE NOT t.tgisinternal
    UNION ALL
    SELECT 'fn ' || p.oid::regprocedure::text || ' ' || md5(coalesce(p.prosrc, ''))
    FROM pg_proc p JOIN ns ON ns.oid = p.pronamespace
    UNION ALL
    SELECT 'type ' || ns.nspname || '.' || t.typname || ' ' || t.typtype::text || ' '
           || coalesce((SELECT string_agg(e.enumlabel, ',' ORDER BY e.enumsortorder)
                        FROM pg_enum e WHERE e.enumtypid = t.oid), '')
    FROM pg_type t JOIN ns ON ns.oid = t.typnamespace
    WHERE t.typrelid = 0 AND t.typcategory <> 'A'
    UNION ALL
    SELECT 'defacl ' || d.defaclrole::regrole::text || ' ' || coalesce(d.defaclnamespace::regnamespace::text, '')
           || ' ' || d.defaclobjtype::text || ' ' || d.defaclacl::text
    FROM pg_default_acl d
  ) objects(x)
$fp$;

CREATE FUNCTION pg_temp.thor_assert_schema(before text) RETURNS void LANGUAGE plpgsql AS $as$
BEGIN
  IF pg_temp.thor_schema_fp() IS DISTINCT FROM before THEN
    RAISE EXCEPTION 'backfill changed the schema: backfills may only change data';
  END IF;
END
$as$;
