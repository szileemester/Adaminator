import { useEffect } from 'react';
import type { ReactNode } from 'react';
import type { Control } from 'react-hook-form';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import {
  Box,
  Button,
  Checkbox,
  FormControlLabel,
  MenuItem,
  Stack,
  TextField,
} from '@mui/material';
import type {
  GroupStageKind,
  MatchFormat,
  PlayoffKind,
  ScoreType,
  TiebreakerPolicy,
  TournamentInput,
  TournamentType,
} from '../api/types';
import {
  groupStageKindLabels,
  matchFormatLabels,
  playoffKindLabels,
  playoffSizes,
  scoreTypeLabels,
  tiebreakerPolicyLabels,
  tournamentSettingsShape,
  tournamentTypeLabels,
  NOTES_MAX_LENGTH,
  TOURNAMENT_NAME_MAX_LENGTH,
} from '../api/types';
import { SectionHeading } from './SectionHeading';

const decisiveFormat = z.enum(['Bo1', 'Bo3', 'Bo5', 'Bo7']);

const schema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(TOURNAMENT_NAME_MAX_LENGTH, `Name must be at most ${TOURNAMENT_NAME_MAX_LENGTH} characters`),
  date: z.string().min(1, 'Date is required'),
  notes: z.string().max(NOTES_MAX_LENGTH, 'Notes are too long').optional(),
  type: z.enum(['SingleElimination', 'DoubleElimination', 'RoundRobin', 'GroupStagePlayoff']),
  // Single Elimination + Round Robin only: their one match format - never Bo2, they have no group stage.
  defaultMatchFormat: decisiveFormat,
  thirdPlaceEnabled: z.boolean(),
  defaultScoreType: z.enum(['Games', 'Points']),
  groupCount: z.number().int('Enter a whole number').min(2, 'At least 2 groups').max(16, 'At most 16 groups'),
  // Group Stage + Playoff only: which of the four variants this is.
  groupStageKind: z.enum(['RoundRobin', 'Swiss']),
  playoffKind: z.enum(['DoubleElimination', 'SingleElimination']),
  // 0 is "Auto" - the largest capacity the roster fills, resolved when the tournament starts.
  playoffSize: z.number().int(),
  // 0 is "Auto" - ceil(log2 roster), resolved when the tournament starts.
  swissRounds: z.number().int('Enter a whole number').min(0, 'Cannot be negative').max(31, 'At most 31 rounds'),
  tiebreakerPolicy: z.enum(['ComputedThenMatch', 'AlwaysMatch']),
  // Group Stage + Playoff only: the one format picker that allows Bo2 (draws, ranked by games won).
  groupStageMatchFormat: z.enum(['Bo1', 'Bo2', 'Bo3', 'Bo5', 'Bo7']),
  // Double Elimination + Group Stage + Playoff only: always decisive, a bracket match must have a winner.
  upperBracketFormat: decisiveFormat,
  lowerBracketFormat: decisiveFormat,
  grandFinalFormat: decisiveFormat,
});

export type TournamentFormValues = z.infer<typeof schema>;

const today = () => new Date().toISOString().slice(0, 10);

const decisiveFormats: MatchFormat[] = ['Bo1', 'Bo3', 'Bo5', 'Bo7'];
const groupStageMatchFormats: MatchFormat[] = ['Bo1', 'Bo2', 'Bo3', 'Bo5', 'Bo7'];
const tournamentTypes: TournamentType[] = ['SingleElimination', 'DoubleElimination', 'RoundRobin', 'GroupStagePlayoff'];
const scoreTypes: ScoreType[] = ['Games', 'Points'];
const tiebreakerPolicies: TiebreakerPolicy[] = ['ComputedThenMatch', 'AlwaysMatch'];
const groupStageKinds: GroupStageKind[] = ['RoundRobin', 'Swiss'];
const playoffKinds: PlayoffKind[] = ['DoubleElimination', 'SingleElimination'];

interface TournamentFormProps {
  initialValues?: Partial<TournamentFormValues>;
  submitLabel: string;
  submitting?: boolean;
  onSubmit: (values: TournamentInput) => void;
  onCancel?: () => void;
}

type FormatFieldName = 'defaultMatchFormat' | 'upperBracketFormat' | 'lowerBracketFormat' | 'grandFinalFormat' | 'groupStageMatchFormat';

/** One match-format dropdown, shared by every format field below - they differ only in name/label/options. */
function FormatPicker({
  name,
  label,
  control,
  options,
  helperText,
}: {
  name: FormatFieldName;
  label: string;
  control: Control<TournamentFormValues>;
  options: MatchFormat[];
  helperText?: string;
}) {
  return (
    <Controller
      name={name}
      control={control}
      render={({ field }) => (
        <TextField select label={label} helperText={helperText} {...field}>
          {options.map((format) => (
            <MenuItem key={format} value={format}>
              {matchFormatLabels[format]}
            </MenuItem>
          ))}
        </TextField>
      )}
    />
  );
}

/**
 * One titled group of fields. Sections are rendered behind an explicit condition rather than by
 * inspecting their children, so a section whose every field is hidden never leaves a stray heading.
 */
function FormSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Stack spacing={2}>
      <SectionHeading title={title} />
      {/* Top-aligned: helper text makes fields differ in height, and a stretched row drags a short
          control (the third-place checkbox) down against its taller neighbour. */}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 3, alignItems: 'start' }}>
        {children}
      </Box>
    </Stack>
  );
}

export function TournamentForm({
  initialValues,
  submitLabel,
  submitting = false,
  onSubmit,
  onCancel,
}: TournamentFormProps) {
  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    formState: { errors },
  } = useForm<TournamentFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: '',
      date: today(),
      notes: '',
      type: 'SingleElimination',
      defaultMatchFormat: 'Bo3',
      thirdPlaceEnabled: false,
      defaultScoreType: 'Games',
      groupCount: 2,
      groupStageKind: 'RoundRobin',
      playoffKind: 'DoubleElimination',
      playoffSize: 0,
      swissRounds: 0,
      tiebreakerPolicy: 'ComputedThenMatch',
      groupStageMatchFormat: 'Bo3',
      upperBracketFormat: 'Bo3',
      lowerBracketFormat: 'Bo3',
      grandFinalFormat: 'Bo3',
      ...initialValues,
    },
  });

  const selectedType = watch('type');
  const {
    isGroupStagePlayoff,
    isRoundRobin,
    usesRoundRobinGroups,
    usesSwissPool,
    playoffIsSingleElimination,
    showsSingleFormat,
    usesBracketFormats,
    showsAllBracketFormats,
    showsTiebreakerPolicy,
    showsRules,
  } = tournamentSettingsShape(selectedType, watch('groupStageKind'), watch('playoffKind'));

  // Third Place Match needs a single-elimination bracket; clear it when switching away from one.
  useEffect(() => {
    if (!playoffIsSingleElimination) {
      setValue('thirdPlaceEnabled', false);
    }
  }, [playoffIsSingleElimination, setValue]);

  // A Swiss pool ranks on match wins, so Best of 2's games-won ranking doesn't apply to it - the one
  // format it cannot keep. Only that one is replaced: this runs on the first render too, so resetting
  // unconditionally would silently rewrite a saved Swiss tournament's format every time Edit is opened,
  // and save the rewrite the moment anything else is changed.
  const groupStageMatchFormat = watch('groupStageMatchFormat');
  useEffect(() => {
    if (usesSwissPool && groupStageMatchFormat === 'Bo2') {
      setValue('groupStageMatchFormat', 'Bo3');
    }
  }, [usesSwissPool, groupStageMatchFormat, setValue]);

  const submit = handleSubmit((values) => {
    // Segments a type doesn't have collapse onto the format it does use, so a hidden picker never
    // posts a stale value.
    const playoffFormat = usesBracketFormats ? values.upperBracketFormat : values.defaultMatchFormat;
    onSubmit({
      name: values.name.trim(),
      date: values.date,
      notes: values.notes?.trim() ? values.notes.trim() : null,
      type: values.type,
      defaultMatchFormat: values.defaultMatchFormat,
      thirdPlaceEnabled: playoffIsSingleElimination && values.thirdPlaceEnabled,
      defaultScoreType: values.defaultScoreType,
      groupCount: usesRoundRobinGroups ? values.groupCount : 0,
      groupStageKind: isGroupStagePlayoff ? values.groupStageKind : 'RoundRobin',
      playoffKind: isGroupStagePlayoff ? values.playoffKind : 'DoubleElimination',
      playoffSize: isGroupStagePlayoff ? values.playoffSize : 0,
      swissRounds: usesSwissPool ? values.swissRounds : 0,
      tiebreakerPolicy: values.tiebreakerPolicy,
      groupStageMatchFormat: isGroupStagePlayoff ? values.groupStageMatchFormat : values.defaultMatchFormat,
      upperBracketFormat: playoffFormat,
      // A single-elimination playoff has no Loser Bracket, so that format collapses onto its one.
      lowerBracketFormat: showsAllBracketFormats ? values.lowerBracketFormat : playoffFormat,
      // Every type but Round Robin ends on a deciding match with its own format.
      grandFinalFormat: isRoundRobin ? values.defaultMatchFormat : values.grandFinalFormat,
    });
  });

  return (
    <form onSubmit={submit} noValidate>
      <Stack spacing={3}>
        {/* Everything a tournament always has, so it reads the same whichever type is chosen. */}
        <FormSection title="Basics">
          {/* maxLength stops typing at the limit rather than only rejecting it on submit; the schema
              still enforces the same number, since a paste or an autofill can bypass the attribute. */}
          <TextField
            label="Name"
            required
            {...register('name')}
            error={Boolean(errors.name)}
            helperText={errors.name?.message}
            slotProps={{ htmlInput: { maxLength: TOURNAMENT_NAME_MAX_LENGTH } }}
          />

          <TextField
            label="Date"
            type="date"
            required
            slotProps={{ inputLabel: { shrink: true } }}
            {...register('date')}
            error={Boolean(errors.date)}
            helperText={errors.date?.message}
          />

          <Controller
            name="type"
            control={control}
            render={({ field }) => (
              <TextField select label="Tournament type" {...field}>
                {tournamentTypes.map((type) => (
                  <MenuItem key={type} value={type}>
                    {tournamentTypeLabels[type]}
                  </MenuItem>
                ))}
              </TextField>
            )}
          />

          <Controller
            name="defaultScoreType"
            control={control}
            render={({ field }) => (
              <TextField select label="Default score type" {...field}>
                {scoreTypes.map((type) => (
                  <MenuItem key={type} value={type}>
                    {scoreTypeLabels[type]}
                  </MenuItem>
                ))}
              </TextField>
            )}
          />
        </FormSection>

        {isGroupStagePlayoff && (
          <FormSection title="Structure">
            <Controller
              name="groupStageKind"
              control={control}
              render={({ field }) => (
                <TextField select label="Group stage" {...field} helperText="How the field is played down before the playoff.">
                  {groupStageKinds.map((kind) => (
                    <MenuItem key={kind} value={kind}>
                      {groupStageKindLabels[kind]}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            <Controller
              name="playoffKind"
              control={control}
              render={({ field }) => (
                <TextField select label="Playoff" {...field} helperText="The bracket the qualifiers play.">
                  {playoffKinds.map((kind) => (
                    <MenuItem key={kind} value={kind}>
                      {playoffKindLabels[kind]}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />

            {usesRoundRobinGroups && (
              <TextField
                label="Number of groups"
                type="number"
                required
                slotProps={{ htmlInput: { min: 2, max: 16, inputMode: 'numeric' } }}
                {...register('groupCount', { valueAsNumber: true })}
                error={Boolean(errors.groupCount)}
                helperText={
                  errors.groupCount?.message ??
                  'Participants are drawn randomly into this many groups; sizes may differ by one.'
                }
              />
            )}

            {usesSwissPool && (
              <TextField
                label="Swiss rounds"
                type="number"
                required
                slotProps={{ htmlInput: { min: 0, max: 31, inputMode: 'numeric' } }}
                {...register('swissRounds', { valueAsNumber: true })}
                error={Boolean(errors.swissRounds)}
                helperText={
                  errors.swissRounds?.message ??
                  '0 picks enough rounds to leave one undefeated leader. Each round is paired from the standings once the previous one is decided.'
                }
              />
            )}

            <Controller
              name="playoffSize"
              control={control}
              render={({ field }) => (
                <TextField
                  select
                  label="Playoff size"
                  {...field}
                  onChange={(e) => field.onChange(Number(e.target.value))}
                  helperText="How many qualify. Auto takes the largest that fits the roster; the rest are knocked out at the group stage."
                >
                  {playoffSizes.map((size) => (
                    <MenuItem key={size} value={size}>
                      {size === 0 ? 'Auto (largest that fits)' : size}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
          </FormSection>
        )}

        {/* Never empty: every type has at least one match to format. */}
        <FormSection title="Match formats">
          {showsSingleFormat && (
            <FormatPicker name="defaultMatchFormat" label="Match format" control={control} options={decisiveFormats} />
          )}

          {isGroupStagePlayoff && (
            <FormatPicker
              name="groupStageMatchFormat"
              label="Group stage match format"
              control={control}
              options={usesSwissPool ? decisiveFormats : groupStageMatchFormats}
              helperText={
                usesSwissPool
                  ? 'A Swiss pool ranks on match wins, so it needs a decisive format.'
                  : 'Best of 2 plays two games per pair (a 1-1 is a draw) and ranks groups by total games won.'
              }
            />
          )}

          {/* A single-elimination playoff has one bracket, so the same picker just changes its label. */}
          {usesBracketFormats && (
            <FormatPicker
              name="upperBracketFormat"
              label={showsAllBracketFormats ? 'Upper bracket format' : 'Playoff match format'}
              control={control}
              options={decisiveFormats}
            />
          )}

          {showsAllBracketFormats && (
            <FormatPicker name="lowerBracketFormat" label="Lower bracket format" control={control} options={decisiveFormats} />
          )}

          {/* Every elimination shape ends on one deciding match that can be played longer than the rest. */}
          {!isRoundRobin && (
            <FormatPicker
              name="grandFinalFormat"
              label={showsAllBracketFormats ? 'Grand Final format' : 'Final format'}
              control={control}
              options={decisiveFormats}
            />
          )}
        </FormSection>

        {showsRules && (
          <FormSection title="Rules">
            {playoffIsSingleElimination && (
              <Controller
                name="thirdPlaceEnabled"
                control={control}
                render={({ field }) => (
                  <FormControlLabel
                    control={<Checkbox checked={field.value} onChange={field.onChange} />}
                    label="Third place match"
                  />
                )}
              />
            )}

            {showsTiebreakerPolicy && (
              <Controller
                name="tiebreakerPolicy"
                control={control}
                render={({ field }) => (
                  <TextField
                    select
                    label="Tie-breaker"
                    {...field}
                    helperText="How a standings tie that changes an outcome (who qualifies, which bracket they enter, or a podium place) is resolved."
                  >
                    {tiebreakerPolicies.map((policy) => (
                      <MenuItem key={policy} value={policy}>
                        {tiebreakerPolicyLabels[policy]}
                      </MenuItem>
                    ))}
                  </TextField>
                )}
              />
            )}
          </FormSection>
        )}

        <TextField
          label="Notes"
          multiline
          minRows={3}
          {...register('notes')}
          error={Boolean(errors.notes)}
          helperText={errors.notes?.message}
        />

        <Stack direction="row" spacing={2}>
          <Button type="submit" variant="contained" disabled={submitting}>
            {submitLabel}
          </Button>
          {onCancel && (
            <Button variant="text" onClick={onCancel} disabled={submitting}>
              Cancel
            </Button>
          )}
        </Stack>
      </Stack>
    </form>
  );
}
