import { useEffect } from 'react';
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
import type { GroupStageFormat, MatchFormat, ScoreType, TiebreakerPolicy, TournamentInput, TournamentType } from '../api/types';
import { groupStageFormatLabels, matchFormatLabels, scoreTypeLabels, tiebreakerPolicyLabels, tournamentTypeLabels } from '../api/types';

const schema = z.object({
  name: z.string().trim().min(1, 'Name is required').max(200, 'Name is too long'),
  date: z.string().min(1, 'Date is required'),
  notes: z.string().max(2000, 'Notes are too long').optional(),
  type: z.enum(['SingleElimination', 'DoubleElimination', 'RoundRobin', 'GroupStagePlayoff']),
  // Bo2 is accepted by the type but never offered in `matchFormats` below: the default format drives
  // the decisive playoff, so the dropdown only lists the odd, always-decisive formats.
  defaultMatchFormat: z.enum(['Bo1', 'Bo2', 'Bo3', 'Bo5', 'Bo7']),
  thirdPlaceEnabled: z.boolean(),
  defaultScoreType: z.enum(['WinnerOnly', 'Games', 'Points', 'Sets']),
  groupCount: z.number().int('Enter a whole number').min(2, 'At least 2 groups').max(16, 'At most 16 groups'),
  tiebreakerPolicy: z.enum(['ComputedThenMatch', 'AlwaysMatch']),
  groupStageFormat: z.enum(['Standard', 'BestOfTwo']),
});

export type TournamentFormValues = z.infer<typeof schema>;

const today = () => new Date().toISOString().slice(0, 10);

const matchFormats: MatchFormat[] = ['Bo1', 'Bo3', 'Bo5', 'Bo7'];
const tournamentTypes: TournamentType[] = ['SingleElimination', 'DoubleElimination', 'RoundRobin', 'GroupStagePlayoff'];
const scoreTypes: ScoreType[] = ['Games', 'Sets', 'Points', 'WinnerOnly'];
const tiebreakerPolicies: TiebreakerPolicy[] = ['ComputedThenMatch', 'AlwaysMatch'];
const groupStageFormats: GroupStageFormat[] = ['Standard', 'BestOfTwo'];

interface TournamentFormProps {
  initialValues?: Partial<TournamentFormValues>;
  submitLabel: string;
  submitting?: boolean;
  onSubmit: (values: TournamentInput) => void;
  onCancel?: () => void;
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
      tiebreakerPolicy: 'ComputedThenMatch',
      groupStageFormat: 'Standard',
      ...initialValues,
    },
  });

  const selectedType = watch('type');
  const isSingleElimination = selectedType === 'SingleElimination';
  const isGroupStagePlayoff = selectedType === 'GroupStagePlayoff';
  // Only Round Robin and Group Stage + Playoff produce round-robin standings that can tie in a way that matters.
  const showsTiebreakerPolicy = selectedType === 'RoundRobin' || isGroupStagePlayoff;
  const selectedFormat = watch('defaultMatchFormat');
  const selectedScoreType = watch('defaultScoreType');
  const isBo1 = selectedFormat === 'Bo1';

  // Third Place Match is Single-Elimination only; clear it when switching away from it.
  useEffect(() => {
    if (!isSingleElimination) {
      setValue('thirdPlaceEnabled', false);
    }
  }, [isSingleElimination, setValue]);

  // Winner Only scoring is valid only for BO1; fall back to Games when switching away from it.
  useEffect(() => {
    if (!isBo1 && selectedScoreType === 'WinnerOnly') {
      setValue('defaultScoreType', 'Games');
    }
  }, [isBo1, selectedScoreType, setValue]);

  const submit = handleSubmit((values) => {
    onSubmit({
      name: values.name.trim(),
      date: values.date,
      notes: values.notes?.trim() ? values.notes.trim() : null,
      type: values.type,
      defaultMatchFormat: values.defaultMatchFormat,
      thirdPlaceEnabled: values.type === 'SingleElimination' && values.thirdPlaceEnabled,
      defaultScoreType: values.defaultMatchFormat !== 'Bo1' && values.defaultScoreType === 'WinnerOnly' ? 'Games' : values.defaultScoreType,
      groupCount: values.type === 'GroupStagePlayoff' ? values.groupCount : 0,
      tiebreakerPolicy: values.tiebreakerPolicy,
      groupStageFormat: values.type === 'GroupStagePlayoff' ? values.groupStageFormat : 'Standard',
    });
  });

  return (
    <form onSubmit={submit} noValidate>
      {/*
        Two columns on md+ so short controls (Name, Type, format/score pickers) don't stretch across
        the whole card - Date/Notes/buttons stay full width via gridColumn: '1 / -1'.
        gridAutoFlow: 'dense' matters here: pinning Third place to column 2 (the last column)
        advances the browser's auto-placement cursor past the row's end, which wraps to the *next*
        row before Date's auto-placed search even starts - sparse packing (the grid default) never
        looks backward to fill the column-1 gap that leaves in Third place's row. Dense packing does.
      */}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gridAutoFlow: 'dense', gap: 3 }}>
        <TextField
          label="Name"
          required
          {...register('name')}
          error={Boolean(errors.name)}
          helperText={errors.name?.message}
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
          name="defaultMatchFormat"
          control={control}
          render={({ field }) => (
            <TextField select label="Default match format" {...field}>
              {matchFormats.map((format) => (
                <MenuItem key={format} value={format}>
                  {matchFormatLabels[format]}
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
                <MenuItem key={type} value={type} disabled={type === 'WinnerOnly' && !isBo1}>
                  {scoreTypeLabels[type]}
                </MenuItem>
              ))}
            </TextField>
          )}
        />

        {isGroupStagePlayoff && (
          <TextField
            label="Number of groups"
            type="number"
            required
            slotProps={{ htmlInput: { min: 2, max: 16, inputMode: 'numeric' } }}
            {...register('groupCount', { valueAsNumber: true })}
            error={Boolean(errors.groupCount)}
            helperText={
              errors.groupCount?.message ??
              'Participants are drawn randomly into this many groups; sizes may differ by one. The largest power of two that fits (4/8/16/32) reaches the playoff - anyone below that is knocked out at the group stage.'
            }
          />
        )}

        {isGroupStagePlayoff && (
          <Controller
            name="groupStageFormat"
            control={control}
            render={({ field }) => (
              <TextField
                select
                label="Group matches"
                {...field}
                helperText="Best of 2 plays two games per pair (a 1-1 is a draw) and ranks groups by total games won. The playoff stays decisive."
              >
                {groupStageFormats.map((format) => (
                  <MenuItem key={format} value={format}>
                    {groupStageFormatLabels[format]}
                  </MenuItem>
                ))}
              </TextField>
            )}
          />
        )}

        <Controller
          name="thirdPlaceEnabled"
          control={control}
          render={({ field }) => (
            <FormControlLabel
              control={<Checkbox checked={field.value} onChange={field.onChange} disabled={!isSingleElimination} />}
              label="Third place match (Single Elimination only)"
              // Pinned to column 2 (rather than left to grid auto-flow) so it doesn't jump into
              // column 1 whenever "Number of groups" above it is absent (non-GSP tournaments).
              sx={{ gridColumn: { xs: '1 / -1', md: '2' } }}
            />
          )}
        />

        {showsTiebreakerPolicy && (
          <Controller
            name="tiebreakerPolicy"
            control={control}
            render={({ field }) => (
              <TextField
                select
                label="Tie-breaker"
                {...field}
                sx={{ gridColumn: '1 / -1' }}
                helperText="How a standings tie that changes an outcome (the Upper/Lower split, or a podium place) is resolved."
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

        <TextField
          label="Date"
          type="date"
          required
          slotProps={{ inputLabel: { shrink: true } }}
          {...register('date')}
          error={Boolean(errors.date)}
          helperText={errors.date?.message}
        />

        <TextField
          label="Notes"
          multiline
          minRows={3}
          {...register('notes')}
          error={Boolean(errors.notes)}
          helperText={errors.notes?.message}
          sx={{ gridColumn: '1 / -1' }}
        />

        <Stack direction="row" spacing={2} sx={{ gridColumn: '1 / -1' }}>
          <Button type="submit" variant="contained" disabled={submitting}>
            {submitLabel}
          </Button>
          {onCancel && (
            <Button variant="text" onClick={onCancel} disabled={submitting}>
              Cancel
            </Button>
          )}
        </Stack>
      </Box>
    </form>
  );
}
